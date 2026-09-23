import sys
import struct
import json
import subprocess
import os

# Native messaging message receiver
def get_message():
    raw_length = sys.stdin.buffer.read(4)
    if not raw_length or len(raw_length) == 0:
        sys.exit(0)
    msg_length = struct.unpack('@I', raw_length)[0]
    message = sys.stdin.buffer.read(msg_length).decode('utf-8')
    return json.loads(message)

# Native messaging message sender
def send_message(message_content):
    encoded_content = json.dumps(message_content).encode('utf-8')
    sys.stdout.buffer.write(struct.pack('@I', len(encoded_content)))
    sys.stdout.buffer.write(encoded_content)
    sys.stdout.buffer.flush()

def main():
    while True:
        try:
            msg = get_message()
            action = msg.get("action", "")
            
            if action in ["start_server", "launch_app"]:
                root_dir = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
                candidate_paths = [
                    os.path.join(root_dir, "Run-App.bat"),
                    os.path.join(root_dir, "SsshmulDownloader", "bin", "Release", "net8.0-windows", "SsshmulDownloader.exe"),
                    os.path.join(root_dir, "SsshmulDownloader", "bin", "Debug", "net8.0-windows", "SsshmulDownloader.exe"),
                    os.path.join(root_dir, "SsshmulDownloader.exe"),
                    r"C:\Program Files\Ssshmul Downloader\SsshmulDownloader.exe",
                    r"C:\Program Files\Ssshmul Downloader\Ssshmul Downloader.exe",
                    r"C:\Users\SHMUEL\AppData\Local\SsshmulDownloader\SsshmulDownloader.exe",
                    r"C:\Users\SHMUEL\AppData\Local\SsshmulDownloader\Ssshmul Downloader.exe"
                ]
                target_exe = next((p for p in candidate_paths if os.path.exists(p)), None)
                
                DETACHED_PROCESS = 0x00000008
                if target_exe:
                    if target_exe.endswith(".bat"):
                        subprocess.Popen([target_exe], creationflags=DETACHED_PROCESS, shell=True)
                    else:
                        subprocess.Popen([target_exe, "--protocol-launch"], creationflags=DETACHED_PROCESS, close_fds=True)
                    send_message({"status": "ok", "message": "Server launched successfully"})
                else:
                    send_message({"status": "error", "message": "Executable/Script not found"})
            elif action in ["stop_server", "shutdown_server"]:
                # Try HTTP request or process kill
                try:
                    import urllib.request
                    urllib.request.urlopen("http://localhost:9595/shutdown", timeout=2)
                    send_message({"status": "ok", "message": "Shutdown signal sent via HTTP"})
                except Exception:
                    # Fallback: taskkill for windows
                    try:
                        subprocess.run(["taskkill", "/f", "/im", "SsshmulDownloader.exe"], capture_output=True)
                        subprocess.run(["taskkill", "/f", "/im", "Ssshmul Downloader.exe"], capture_output=True)
                    except Exception:
                        pass
                    send_message({"status": "ok", "message": "Shutdown signal processed"})
            else:
                send_message({"status": "ok", "message": "Acknowledged"})
        except Exception as e:
            send_message({"status": "error", "error": str(e)})
            break

if __name__ == '__main__':
    main()
