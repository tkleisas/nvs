import subprocess, json, time, re, threading

EXE = r'C:\Users\mprizols\AppData\Roaming\NVS\tools\jdtls\jdtls.cmd'
CWD = r'C:\Users\mprizols\AppData\Local\Temp\nvs-lang-samples\hello-java'
ROOT = 'file:///C:/Users/mprizols/AppData/Local/Temp/nvs-lang-samples/hello-java'

NVS_CAPS = {
    "textDocument": {
        "hover": {},
        "definition": {},
        "references": {},
        "documentSymbol": {"hierarchicalDocumentSymbolSupport": True},
        "formatting": {},
        "publishDiagnostics": {"relatedInformation": True},
        "synchronization": {"didSave": True, "didChange": True},
        "signatureHelp": {
            "contextSupport": True,
            "signatureInformation": {"activeParameterSupport": True},
        },
        "codeAction": {
            "codeActionLiteralSupport": {
                "codeActionKind": {
                    "valueSet": ["quickfix", "refactor", "refactor.extract", "refactor.inline",
                                 "refactor.rewrite", "source", "source.organizeImports", "source.fixAll"]
                }
            },
            "isPreferredSupport": True,
        },
    }
}

def roundtrip(params, label):
    proc = subprocess.Popen([EXE], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, cwd=CWD)
    buf = bytearray()
    def pump():
        while True:
            chunk = proc.stdout.read1(65536)
            if not chunk:
                break
            buf.extend(chunk)
    t = threading.Thread(target=pump, daemon=True)
    t.start()

    req = {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": params}
    body = json.dumps(req).encode()
    proc.stdin.write(b'Content-Length: ' + str(len(body)).encode() + b'\r\n\r\n' + body)
    proc.stdin.flush()

    deadline = time.time() + 50
    outcome = 'timeout'
    while time.time() < deadline:
        data = bytes(buf)
        if b'"id":1' in data and (b'"result"' in data or b'"error"' in data):
            outcome = 'error' if b'"error"' in data else 'ok'
            break
        time.sleep(0.25)

    print(f'{label}: {outcome}')
    if outcome == 'error':
        m = re.search(rb'"message":"([^"]{0,200})', bytes(buf))
        print('   ', m.group(1).decode() if m else bytes(buf)[:200])
    subprocess.run(['powershell', '-NoProfile', '-Command',
                    "Get-CimInstance Win32_Process -Filter \"Name='java.exe'\" | "
                    "Where-Object { $_.CommandLine -like '*jdt.ls*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"],
                   capture_output=True)

roundtrip({"processId": 1234, "rootUri": ROOT, "capabilities": {}}, 'minimal caps : ')
roundtrip({"processId": 1234, "rootUri": ROOT, "capabilities": NVS_CAPS}, 'full NVS caps: ')
