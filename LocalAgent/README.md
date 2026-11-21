# Local Agent: an AI assistant that runs on your PC with Ollama

Local Agent is a chat assistant that can actually do things on your computer.
The AI model runs on your own PC through [Ollama](https://ollama.com). It needs
no cloud account or API key, and your files never leave your machine.

Ask it things like:

* "What files are in here, and which ones are the biggest?"
* "Read notes.txt and turn it into a to-do list in todo.md."
* "Find every .csv file and tell me how many rows each one has."
* "Make a folder for each month of 2027."

## What it can do

| Tool | What it does | Asks you first? |
|---|---|---|
| `list_files` | Lists a folder | No |
| `read_file` | Reads a text file | No |
| `find_files` | Finds files by name, such as `*.pdf` | No |
| `write_file` | Creates or replaces a text file, and shows you a preview | **Yes** |
| `run_command` | Runs a command (Command Prompt on Windows) | **Yes** |

**Safety:** the agent can only touch **one work folder**: the `workspace` folder
next to `agent.py`, or a folder you choose with `--folder`. It can't read or
write files outside it. Before it writes any file or runs any command, it shows
you exactly what it will do and waits for you to type `y`.

## Set it up (once)

1. Install **Python 3.10 or newer** from [python.org](https://www.python.org).
2. Install **Ollama** from [ollama.com](https://ollama.com/download) and open it.
3. Double-click **`agent.bat`**. The first time, it installs the `ollama` Python
   package and downloads the model (`qwen3`, about 5 GB).

After that, double-click `agent.bat` whenever you want to use it. Type `exit` to
quit and `reset` to start a new conversation.

## Options

```
python agent.py                              chat, work folder = .\workspace
python agent.py --folder "C:\Users\me\Notes" work in another folder
python agent.py --model llama3.2             use another model
python agent.py check                        check that Ollama and the model are ready
```

The same options work with `agent.bat`, for example
`agent.bat --folder "C:\Users\me\Notes"`. You can also set the `OLLAMA_MODEL`
environment variable to change the default model.

## Choosing a model

The model must support **tools** ([list on ollama.com](https://ollama.com/search?c=tools)).

| Model | Download | Good for |
|---|---|---|
| `qwen3` (default) | ~5 GB | Most PCs with 16 GB of RAM. Good at using tools |
| `llama3.2` | ~2 GB | Older or low-memory PCs. Faster, less capable |
| `qwen3:14b` | ~9 GB | PCs with a strong graphics card. Smarter, slower |

Download a model with `ollama pull <name>` and use it with `--model <name>`.
Small models make more mistakes with tools. If the agent gets confused, try a
bigger model or ask in smaller steps.

## How it works

`agent.py` is about 300 lines of plain Python:

1. Your message and the list of tools go to Ollama (`ollama.chat`).
2. If the model asks for a tool, the agent runs it (after asking you, for writes
   and commands) and sends the result back.
3. It repeats until the model answers in plain text, up to 20 steps per message.

To add a tool, write a method on `Agent` with a clear docstring (the model reads
it) and add it to `self.tools`.

## Tests

```
pip install -r requirements.txt pytest
python -m pytest tests
```

The tests start a small fake Ollama server, so they need neither Ollama nor
a model download.
