"""Local AI agent: an assistant that runs on your PC with Ollama.

The model runs on your own computer through Ollama (no cloud, no API key).
The agent can look at, read and write files in one work folder, and run
commands there. It always asks you before it writes a file or runs a command.

    python agent.py                      chat, work folder = ./workspace
    python agent.py --folder C:\\Notes    let it work in another folder
    python agent.py --model llama3.2     use a different Ollama model
    python agent.py check                check that Ollama and the model are ready
"""

from __future__ import annotations

import argparse
import datetime as dt
import os
import pathlib
import platform
import subprocess
import sys

import ollama

DEFAULT_MODEL = os.environ.get("OLLAMA_MODEL", "qwen3")
MAX_STEPS = 20          # tool calls the agent may make for one message
MAX_OUTPUT = 8000       # characters of a file or command output sent to the model
COMMAND_TIMEOUT = 120   # seconds before a command is stopped

SYSTEM_PROMPT = """You are a helpful assistant running on the user's own {os} computer.
You can use tools to work with files in the work folder: {folder}
All file paths are relative to that folder. Today is {today}.

- Use the tools when the user asks about their files or wants something done.
  Look before you act: list or read files before changing them.
- run_command runs a {shell} command in the work folder.
- The user must approve every file write and every command. If they say no,
  don't try again the same way; ask what they want instead.
- When you are done, answer briefly in plain language."""


class Agent:
    """Holds the conversation, the tools and the folder they may touch."""

    def __init__(self, folder: pathlib.Path, model: str = DEFAULT_MODEL,
                 client: ollama.Client | None = None, ask=input, out=print,
                 num_ctx: int = 16384):
        self.folder = folder.resolve()
        self.model = model
        self.client = client or ollama.Client()
        self.ask = ask
        self.out = out
        self.options = {"num_ctx": num_ctx}
        self.tools = [self.list_files, self.read_file, self.write_file,
                      self.run_command, self.find_files]
        self.tool_map = {t.__name__: t for t in self.tools}
        windows = platform.system() == "Windows"
        self.messages: list = [{"role": "system", "content": SYSTEM_PROMPT.format(
            os=platform.system() or "unknown", folder=self.folder,
            today=dt.date.today().isoformat(),
            shell="Windows Command Prompt (cmd)" if windows else "shell")}]

    # ------------------------------------------------------------ tools
    # The docstrings below are what the model sees, so keep them clear.

    def list_files(self, path: str = ".") -> str:
        """List the files and folders inside a folder.

        Args:
          path: Folder to list, relative to the work folder. Use "." for the work folder itself.

        Returns:
          One entry per line. Folders end with a slash.
        """
        target = self._safe_path(path)
        if not target.is_dir():
            return f"Error: {path} is not a folder."
        entries = sorted(target.iterdir(), key=lambda p: (not p.is_dir(), p.name.lower()))
        lines = [f"{p.name}/" if p.is_dir() else f"{p.name}  ({p.stat().st_size} bytes)"
                 for p in entries[:500]]
        if len(entries) > 500:
            lines.append(f"... and {len(entries) - 500} more")
        return "\n".join(lines) or "(empty folder)"

    def read_file(self, path: str) -> str:
        """Read a text file.

        Args:
          path: File to read, relative to the work folder.

        Returns:
          The text of the file.
        """
        target = self._safe_path(path)
        if not target.is_file():
            return f"Error: {path} is not a file."
        return _clip(target.read_text(encoding="utf-8", errors="replace"))

    def write_file(self, path: str, content: str) -> str:
        """Create or replace a text file. The user is asked to approve it first.

        Args:
          path: File to write, relative to the work folder. Missing folders are created.
          content: The full text to put in the file.

        Returns:
          Whether the file was written.
        """
        target = self._safe_path(path)
        action = "Replace" if target.exists() else "Create"
        preview = content if len(content) <= 600 else content[:600] + "\n..."
        self.out(f"\n--- {action} {target} ({len(content)} characters) ---\n{preview}\n---")
        if not self._approve(f"{action} this file?"):
            return "The user said no. The file was not written."
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding="utf-8")
        return f"Wrote {path}."

    def run_command(self, command: str) -> str:
        """Run a command in the work folder. The user is asked to approve it first.

        Args:
          command: The command line to run.

        Returns:
          The exit code and what the command printed.
        """
        self.out(f"\n--- Run in {self.folder} ---\n{command}\n---")
        if not self._approve("Run this command?"):
            return "The user said no. The command was not run."
        try:
            result = subprocess.run(command, shell=True, cwd=self.folder, capture_output=True,
                                    text=True, errors="replace", timeout=COMMAND_TIMEOUT)
        except subprocess.TimeoutExpired:
            return f"Error: the command was stopped after {COMMAND_TIMEOUT} seconds."
        output = (result.stdout or "") + (result.stderr or "")
        return _clip(f"Exit code {result.returncode}\n{output}".strip())

    def find_files(self, pattern: str) -> str:
        """Find files by name anywhere inside the work folder.

        Args:
          pattern: A name pattern such as "*.txt" or "*report*".

        Returns:
          Matching paths, one per line.
        """
        matches = [str(p.relative_to(self.folder)) for p in self.folder.rglob(pattern)]
        if not matches:
            return "No files match."
        extra = f"\n... and {len(matches) - 200} more" if len(matches) > 200 else ""
        return "\n".join(sorted(matches)[:200]) + extra

    # ------------------------------------------------------------ loop

    def send(self, text: str) -> str:
        """Give the agent one message and let it work until it answers."""
        self.messages.append({"role": "user", "content": text})
        for _ in range(MAX_STEPS):
            response = self.client.chat(model=self.model, messages=self.messages,
                                        tools=self.tools, options=self.options)
            message = response.message
            self.messages.append(message)
            if not message.tool_calls:
                return message.content or ""
            for call in message.tool_calls:
                name = call.function.name
                self.out(f"  [tool] {name}({_short_args(call.function.arguments)})")
                result = self._call_tool(name, call.function.arguments or {})
                self.messages.append({"role": "tool", "content": result, "tool_name": name})
        return f"(Stopped after {MAX_STEPS} steps. Ask me to continue if you want.)"

    def _call_tool(self, name: str, args: dict) -> str:
        tool = self.tool_map.get(name)
        if tool is None:
            return f"Error: there is no tool called {name}."
        try:
            return str(tool(**args))
        except TypeError as e:
            return f"Error: wrong arguments for {name}: {e}"
        except Exception as e:  # report tool failures to the model instead of crashing
            return f"Error: {type(e).__name__}: {e}"

    def _safe_path(self, path: str) -> pathlib.Path:
        target = (self.folder / path).resolve()
        if target != self.folder and self.folder not in target.parents:
            raise PermissionError(f"{path} is outside the work folder {self.folder}.")
        return target

    def _approve(self, question: str) -> bool:
        return self.ask(f"{question} [y/N] ").strip().lower() in ("y", "yes")


def _clip(text: str) -> str:
    if len(text) <= MAX_OUTPUT:
        return text
    return text[:MAX_OUTPUT] + f"\n... ({len(text) - MAX_OUTPUT} more characters cut)"


def _short_args(args) -> str:
    parts = []
    for key, value in (args or {}).items():
        value = str(value).replace("\n", " ")
        parts.append(f"{key}={value[:60] + '...' if len(value) > 60 else value!r}")
    return ", ".join(parts)


# ---------------------------------------------------------------- command line


def check(model: str, client: ollama.Client) -> bool:
    """Say whether Ollama is running and the model is downloaded."""
    try:
        installed = [m.model for m in client.list().models]
    except ConnectionError:
        print("Ollama is not running. Install it from ollama.com and open the Ollama app.")
        return False
    names = {n for m in installed for n in (m, m.removesuffix(":latest"))}
    if model not in names:
        print(f"Ollama is running, but the model '{model}' is not downloaded.")
        print(f"Download it with:  ollama pull {model}")
        if installed:
            print("Models you have: " + ", ".join(installed))
        return False
    print(f"Ready: Ollama is running and '{model}' is downloaded.")
    return True


def chat(agent: Agent) -> None:
    print(f"Local agent using '{agent.model}'. Work folder: {agent.folder}")
    print("Type a message. Type 'exit' to quit, 'reset' to start a new conversation.\n")
    first_system = agent.messages[0]
    while True:
        try:
            text = input("You: ").strip()
        except (EOFError, KeyboardInterrupt):
            print()
            return
        if text.lower() in ("exit", "quit"):
            return
        if text.lower() == "reset":
            agent.messages = [first_system]
            print("Started a new conversation.\n")
            continue
        if not text:
            continue
        start = len(agent.messages)
        try:
            reply = agent.send(text)
        except (ConnectionError, ollama.ResponseError, KeyboardInterrupt) as e:
            del agent.messages[start:]  # forget the message that failed
            if isinstance(e, ConnectionError):
                print("Lost the connection to Ollama. Is the Ollama app running?\n")
            elif isinstance(e, ollama.ResponseError):
                print(_explain(e, agent.model) + "\n")
            else:
                print("\n(Stopped.)\n")
            continue
        print(f"\nAgent: {reply}\n")


def _explain(error: ollama.ResponseError, model: str) -> str:
    text = str(error.error)
    if error.status_code == 404 or "not found" in text:
        return f"The model '{model}' is not downloaded. Run:  ollama pull {model}"
    if "does not support tools" in text:
        return (f"The model '{model}' can't use tools. Pick one that can, such as "
                "qwen3, llama3.1 or llama3.2 (see ollama.com/search?c=tools).")
    return f"Ollama error: {text}"


def main(argv: list[str] | None = None) -> int:
    here = pathlib.Path(__file__).resolve().parent
    parser = argparse.ArgumentParser(description="An AI agent that runs on your PC with Ollama.")
    parser.add_argument("action", nargs="?", choices=["chat", "check"], default="chat")
    parser.add_argument("--model", default=DEFAULT_MODEL,
                        help=f"Ollama model to use (default: {DEFAULT_MODEL})")
    parser.add_argument("--folder", default=str(here / "workspace"),
                        help="the only folder the agent may read, write and run commands in")
    parser.add_argument("--host", default=None,
                        help="Ollama address if it is not on this PC (default: http://localhost:11434)")
    parser.add_argument("--context", type=int, default=16384,
                        help="how many tokens the model remembers (default: 16384)")
    args = parser.parse_args(argv)

    client = ollama.Client(host=args.host)
    if args.action == "check":
        return 0 if check(args.model, client) else 1
    if not check(args.model, client):
        return 1
    folder = pathlib.Path(args.folder).expanduser()
    folder.mkdir(parents=True, exist_ok=True)
    chat(Agent(folder, model=args.model, client=client, num_ctx=args.context))
    return 0


if __name__ == "__main__":
    sys.exit(main())
