"""Exercise the real Spectre prompts in a POSIX pseudo-terminal after a Release build."""
import os
from pathlib import Path
import pty
import select
import shutil
import signal
import struct
import tempfile
import termios
import time
import fcntl
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
HOST = os.environ.get("DOTNET_HOST_PATH") or shutil.which("dotnet")
CLI = ROOT / "Core2RDLC/bin/Release/net8.0-windows/Core2RDLC.dll"
FIXTURE = ROOT / "Core2RDLC.Tests/bin/Release/net8.0-windows/Core2RDLC.Tests.dll"
NS = "http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition"


class Terminal:
    def __init__(self, args, ci="false"):
        self.pid, self.fd = pty.fork()
        if self.pid == 0:
            os.chdir(ROOT)
            os.environ.update(TERM="xterm-256color", CI=ci)
            os.execv(HOST, [HOST, str(CLI), *map(str, args)])
        fcntl.ioctl(self.fd, termios.TIOCSWINSZ, struct.pack("HHHH", 40, 140, 0, 0))
        self.output = b""
        self.position = 0
        self.closed = False
        self.status = None

    def read(self):
        if not self.closed and select.select([self.fd], [], [], 0.1)[0]:
            try:
                data = os.read(self.fd, 65536)
            except OSError:
                data = b""
            self.closed = not data
            self.output += data
            # Respond as a terminal emulator if the library asks for cursor position.
            if b"\x1b[6n" in data:
                self.send(b"\x1b[1;1R")

    def expect(self, text):
        needle = text.encode()
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            index = self.output.find(needle, self.position)
            if index >= 0:
                self.position = index + len(needle)
                return
            self.read()
            if self.closed:
                break
        raise AssertionError(f"Missing prompt: {text}\n{self.output.decode(errors='replace')}")

    def send(self, value):
        os.write(self.fd, value)

    def wait(self):
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            self.read()
            pid, status = os.waitpid(self.pid, os.WNOHANG)
            if pid:
                self.status = status
                return os.waitstatus_to_exitcode(status)
        raise AssertionError("Interactive CLI did not exit")

    def close(self):
        if self.status is None:
            os.kill(self.pid, signal.SIGKILL)
            os.waitpid(self.pid, 0)
        os.close(self.fd)


def choose_customer(terminal, retry=False, multiple=False):
    terminal.expect("搜尋 class")
    if retry:
        terminal.send(b"NoSuchClassName\r")
        terminal.expect("沒有符合的 class")
        terminal.expect("搜尋 class")
    terminal.send(b"Fixtures.\r" if multiple else b"Fixtures.Customer\r")
    terminal.expect("選擇要加入或更新的 class")
    terminal.expect("Fixtures.Customer")
    if multiple:
        terminal.expect("Fixtures.Order")
    terminal.send(b" \x1b[B \r" if multiple else b" \r")


assert HOST and CLI.exists() and FIXTURE.exists(), "Build the solution in Release and put dotnet on PATH first"
with tempfile.TemporaryDirectory(prefix="core2rdlc-interactive-") as directory:
    report = Path(directory) / "report.rdlc"
    original = f'<Report xmlns="{NS}" xmlns:rd="http://schemas.microsoft.com/SQLServer/reporting/reportdesigner"><DataSources/><DataSets/></Report>'
    for action in ("decline", "save", "multi", "preview", "cancel", "cancel_selection", "ci"):
        report.write_text(original)
        args = ["edit", report, "--assembly", FIXTURE]
        if action == "preview":
            args.append("--preview")
        if action == "ci":
            args.append("--yes")
        terminal = Terminal(args, ci="true" if action == "ci" else "false")
        try:
            if action == "ci":
                assert terminal.wait() != 0
                assert b"--type" in terminal.output
            elif action in ("cancel", "cancel_selection"):
                terminal.expect("搜尋 class")
                if action == "cancel_selection":
                    terminal.send(b"Fixtures.Customer\r")
                    terminal.expect("選擇要加入或更新的 class")
                terminal.send(b"\x03")
                assert terminal.wait() != 0
            else:
                choose_customer(terminal, retry=action == "decline", multiple=action == "multi")
                if action != "preview":
                    terminal.expect("是否儲存以上變更")
                    terminal.send(b"y\r" if action in ("save", "multi") else b"\r")
                assert terminal.wait() == 0, terminal.output.decode(errors="replace")
            if action in ("save", "multi"):
                root = ET.parse(report).getroot()
                datasets = root.findall(f"{{{NS}}}DataSets/{{{NS}}}DataSet")
                expected = ["Customer", "Order"] if action == "multi" else ["Customer"]
                assert [node.attrib["Name"] for node in datasets] == expected
                fields = datasets[0].findall(f"{{{NS}}}Fields/{{{NS}}}Field")
                assert [node.attrib["Name"] for node in fields] == ["Id", "Name"]
            else:
                assert report.read_text() == original, f"{action} changed the file"
        finally:
            terminal.close()
        print(f"PASS interactive {action}")
