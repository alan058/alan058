"""ProcWatch must never use the network. These checks fail if code that could is added."""

import ast
import pathlib

SRC = pathlib.Path(__file__).resolve().parent.parent / "procwatch"
NETWORK_MODULES = {
    "urllib", "http", "requests", "httpx", "aiohttp", "ssl", "ftplib", "smtplib",
    "poplib", "imaplib", "telnetlib", "xmlrpc", "websocket", "websockets", "asyncio",
    "webbrowser", "socketserver",
}
# socket is imported only for the SOCK_STREAM constant; these would open connections.
SOCKET_CALLS = {"socket", "create_connection", "create_server", "socketpair", "fromfd",
                "getaddrinfo", "gethostbyname"}


def _files():
    files = sorted(SRC.glob("*.py"))
    assert files
    return files


def test_no_network_modules_imported():
    for path in _files():
        for node in ast.walk(ast.parse(path.read_text(encoding="utf-8"))):
            names = []
            if isinstance(node, ast.Import):
                names = [a.name for a in node.names]
            elif isinstance(node, ast.ImportFrom) and node.module and node.level == 0:
                names = [node.module]
            for name in names:
                assert name.split(".")[0] not in NETWORK_MODULES, f"{path.name} imports {name}"


def test_socket_used_only_for_constants():
    for path in _files():
        for node in ast.walk(ast.parse(path.read_text(encoding="utf-8"))):
            if (isinstance(node, ast.Attribute) and isinstance(node.value, ast.Name)
                    and node.value.id == "socket"):
                assert node.attr not in SOCKET_CALLS, f"{path.name} calls socket.{node.attr}"
