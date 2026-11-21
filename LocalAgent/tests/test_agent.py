"""Tests for the local agent. They need no Ollama: a small fake Ollama server
answers on this PC, so the real ollama library is tested end to end."""

import http.server
import json
import pathlib
import sys
import threading

import ollama
import pytest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
import agent as ag  # noqa: E402


class FakeOllama:
    """Answers /api/chat with scripted replies and records every request."""

    def __init__(self, replies, models=("qwen3:latest",)):
        self.replies = list(replies)
        self.models = models
        self.requests = []
        fake = self

        class Handler(http.server.BaseHTTPRequestHandler):
            def log_message(self, *args):
                pass

            def _send(self, status, body):
                data = json.dumps(body).encode()
                self.send_response(status)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)

            def do_GET(self):
                if self.path == "/api/tags":
                    self._send(200, {"models": [{"model": m, "name": m} for m in fake.models]})
                else:
                    self._send(404, {"error": "not found"})

            def do_POST(self):
                body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
                fake.requests.append(body)
                reply = fake.replies.pop(0)
                if "error" in reply:
                    self._send(reply.get("status", 400), {"error": reply["error"]})
                    return
                self._send(200, {"model": body["model"], "done": True,
                                 "message": {"role": "assistant", **reply}})

        self.server = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        threading.Thread(target=self.server.serve_forever, daemon=True).start()
        self.client = ollama.Client(host=f"http://127.0.0.1:{self.server.server_port}")

    def close(self):
        self.server.shutdown()


def tool_call(name, **arguments):
    return {"content": "", "tool_calls": [{"function": {"name": name, "arguments": arguments}}]}


@pytest.fixture
def make_agent(tmp_path):
    servers = []

    def make(replies, answers=(), models=("qwen3:latest",)):
        fake = FakeOllama(replies, models)
        servers.append(fake)
        answers = list(answers)
        printed = []
        agent = ag.Agent(tmp_path, client=fake.client, ask=lambda q: answers.pop(0),
                         out=printed.append)
        return agent, fake, printed

    yield make
    for s in servers:
        s.close()


# ---------------------------------------------------------------- the loop


def test_plain_answer(make_agent):
    agent, fake, _ = make_agent([{"content": "Hello!"}])
    assert agent.send("hi") == "Hello!"
    request = fake.requests[0]
    assert request["model"] == "qwen3"
    assert request["messages"][0]["role"] == "system"
    assert request["messages"][-1] == {"role": "user", "content": "hi"}
    assert {t["function"]["name"] for t in request["tools"]} == {
        "list_files", "read_file", "write_file", "run_command", "find_files"}
    assert request["options"]["num_ctx"] == 16384


def test_tool_result_goes_back_to_model(make_agent, tmp_path):
    (tmp_path / "notes.txt").write_text("buy milk", encoding="utf-8")
    agent, fake, printed = make_agent([tool_call("read_file", path="notes.txt"),
                                       {"content": "Your note says: buy milk."}])
    assert agent.send("what is in my notes?") == "Your note says: buy milk."
    second = fake.requests[1]["messages"]
    assert second[-2]["tool_calls"][0]["function"]["name"] == "read_file"
    assert second[-1] == {"role": "tool", "content": "buy milk", "tool_name": "read_file"}
    assert any("[tool] read_file" in line for line in printed)


def test_conversation_is_remembered(make_agent):
    agent, fake, _ = make_agent([{"content": "Nice to meet you."}, {"content": "Alan."}])
    agent.send("My name is Alan.")
    agent.send("What's my name?")
    roles = [m["role"] for m in fake.requests[1]["messages"]]
    assert roles == ["system", "user", "assistant", "user"]


def test_stops_after_max_steps(make_agent, monkeypatch):
    monkeypatch.setattr(ag, "MAX_STEPS", 3)
    agent, fake, _ = make_agent([tool_call("list_files", path=".")] * 3)
    assert "Stopped after 3 steps" in agent.send("loop forever")
    assert len(fake.requests) == 3


def test_unknown_tool_and_bad_arguments(make_agent):
    agent, fake, _ = make_agent([tool_call("delete_everything"),
                                 tool_call("read_file", wrong="x"),
                                 {"content": "ok"}])
    agent.send("go")
    results = [m["content"] for m in fake.requests[2]["messages"] if m["role"] == "tool"]
    assert "no tool called delete_everything" in results[0]
    assert "wrong arguments for read_file" in results[1]


# ---------------------------------------------------------------- tools


def test_write_file_needs_approval(make_agent, tmp_path):
    agent, _, _ = make_agent([], answers=["n", "y"])
    assert "said no" in agent.write_file("a/b.txt", "hello")
    assert not (tmp_path / "a" / "b.txt").exists()
    assert agent.write_file("a/b.txt", "hello") == "Wrote a/b.txt."
    assert (tmp_path / "a" / "b.txt").read_text(encoding="utf-8") == "hello"


def test_run_command_needs_approval(make_agent, tmp_path):
    agent, _, _ = make_agent([], answers=["", "yes"])
    assert "said no" in agent.run_command("echo hi")
    result = agent.run_command("echo hi")
    assert result.startswith("Exit code 0") and "hi" in result


def test_run_command_uses_work_folder(make_agent, tmp_path):
    agent, _, _ = make_agent([], answers=["y"])
    agent.run_command(f'"{sys.executable}" -c "open(\'made.txt\', \'w\').close()"')
    assert (tmp_path / "made.txt").exists()


def test_cannot_leave_work_folder(make_agent, tmp_path):
    agent, _, _ = make_agent([])
    outside = tmp_path.parent / "secret.txt"
    outside.write_text("secret", encoding="utf-8")
    for path in ["../secret.txt", str(outside)]:
        assert "outside the work folder" in agent._call_tool("read_file", {"path": path})
    assert "outside the work folder" in agent._call_tool("list_files", {"path": ".."})


def test_list_and_find_files(make_agent, tmp_path):
    (tmp_path / "docs").mkdir()
    (tmp_path / "docs" / "report.txt").write_text("x", encoding="utf-8")
    (tmp_path / "a.md").write_text("hi", encoding="utf-8")
    agent, _, _ = make_agent([])
    assert agent.list_files(".").splitlines() == ["docs/", "a.md  (2 bytes)"]
    assert agent.find_files("*.txt") == str(pathlib.Path("docs/report.txt"))
    assert agent.find_files("*.pdf") == "No files match."


def test_long_output_is_clipped(make_agent, tmp_path):
    (tmp_path / "big.txt").write_text("x" * (ag.MAX_OUTPUT + 50), encoding="utf-8")
    agent, _, _ = make_agent([])
    assert agent.read_file("big.txt").endswith("(50 more characters cut)")


# ---------------------------------------------------------------- setup checks


def test_check_ready_and_missing_model(capsys):
    fake = FakeOllama([], models=("qwen3:latest", "llama3.2:3b"))
    try:
        assert ag.check("qwen3", fake.client)
        assert ag.check("llama3.2:3b", fake.client)
        assert not ag.check("mistral", fake.client)
        assert "ollama pull mistral" in capsys.readouterr().out
    finally:
        fake.close()


def test_check_without_ollama(capsys):
    assert not ag.check("qwen3", ollama.Client(host="http://127.0.0.1:9"))
    assert "not running" in capsys.readouterr().out


def test_model_without_tools_is_explained(make_agent):
    agent, _, _ = make_agent([{"error": "registry.ollama.ai/library/gemma:latest does not support tools"}])
    with pytest.raises(ollama.ResponseError) as e:
        agent.send("hi")
    assert "can't use tools" in ag._explain(e.value, "gemma")


def test_chat_loop(make_agent, monkeypatch, capsys):
    agent, _, _ = make_agent([{"content": "Hi there."}])
    lines = iter(["hello", "reset", "exit"])
    monkeypatch.setattr("builtins.input", lambda prompt="": next(lines))
    ag.chat(agent)
    out = capsys.readouterr().out
    assert "Agent: Hi there." in out and "new conversation" in out
    assert len(agent.messages) == 1
