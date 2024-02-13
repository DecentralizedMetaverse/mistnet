import subprocess
import os
from concurrent.futures import ThreadPoolExecutor
import threading

exe_file = "MistNet.exe"
num = 20

running_processes = []

def run_exe():    
    process = subprocess.Popen([exe_file], cwd=os.getcwd())
    running_processes.append(process)
    process.wait() 

def wait_for_exit_command():    
    input("プロセスを終了させるにはEnterキーを押してください...\n")
    for process in running_processes:
        process.kill()
    print("全てのプロセスが強制終了されました。")
    

# キー入力を待つThreadを開始
exit_thread = threading.Thread(target=wait_for_exit_command)
exit_thread.start()


with ThreadPoolExecutor(max_workers=num) as executor:
    for i in range(num):
        executor.submit(run_exe)                


exit_thread.join()
