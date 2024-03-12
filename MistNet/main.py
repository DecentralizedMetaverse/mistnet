import asyncio
import json
import random
import websockets
import logging
import datetime
from websockets.exceptions import ConnectionClosed
import os
from ordered_set import OrderedSet

class WebSocketServer:
    def __init__(self):
        self.client_websocket_dict = {}
        self.client_by_id_dict = {}
        self.broadcast = asyncio.Queue()
        self.signaling_request_ids = OrderedSet()
        
        # ログ設定        
        current_datetime = datetime.datetime.now()
        formatted_datetime = current_datetime.strftime('%Y-%m-%d-%H-%M-%S')
        log_dir = "logs"
        os.makedirs(log_dir, exist_ok=True)
        logging.basicConfig(filename=f"{log_dir}/{formatted_datetime}.log", level=logging.INFO, format='%(asctime)s %(message)s')
        
        # 評価
        self.evaluation = {}
        self.evaluation_interval = 5

    async def start_server(self):
        asyncio.create_task(self.save_evaluation_periodically())
        async with websockets.serve(self.handle_websocket, "localhost", 8080):
            await asyncio.Future()  # Run the server indefinitely

    async def handle_websocket(self, websocket, path):
        try:
            async for message in websocket:
                await self.process_message(websocket, message)
        except ConnectionClosed:
            # クライアントが切断した場合の処理
            self.remove_client(websocket)

    async def process_message(self, websocket, message):
        logging.info(f"[RECV] {message}")
        data = json.loads(message)
        client_id = data.get("id")

        if websocket not in self.client_websocket_dict:
            self.add_client(websocket, client_id)

        message_type = data["type"]
        if message_type == "evaluation":
            await self.handle_evaluation(data)
        elif message_type == "signaling_request":
            await self.handle_signaling_request(data)            
        else:
            target_id = data["target_id"]
            client = self.client_by_id_dict[target_id]
            await self.send_message(client, data)                     

    def add_client(self, websocket, client_id):
        self.client_websocket_dict[websocket] = client_id
        self.client_by_id_dict[client_id] = websocket

    def remove_client(self, websocket):
        client_id = self.client_websocket_dict.pop(websocket, None)
        if client_id:            
            del self.client_by_id_dict[client_id]                        
            self.signaling_request_ids.remove(client_id)

    async def handle_signaling_request(self, data):
        client_id = data["id"]        
        
        if len(self.signaling_request_ids) > 0:
            client = self.client_by_id_dict[client_id]
            # randomに選択
            index = random.randint(0, len(self.signaling_request_ids) - 1)
            target_id = self.signaling_request_ids[index]
            await self.send_message(client, {"type": "signaling_response", "target_id": target_id, "request": "offer"})                        
        
        self.signaling_request_ids.add(client_id)    

    async def handle_evaluation(self, data):
        client_id = data["id"]
        location = data["location"]
        time = datetime.datetime.now()
        
        # 時刻を1秒単位に丸める
        rounded_time = time.replace(microsecond=0)
        rounded_time = rounded_time.strftime('%Y-%m-%d %H:%M:%S')

        # 丸めた時刻をキーとして、全クライアントの位置を記録
        if rounded_time not in self.evaluation:
            self.evaluation[rounded_time] = {}
        self.evaluation[rounded_time][client_id] = location

    async def send_message(self, client, data):
        logging.info(f"[SEND] {data}")
        try:
            await client.send(json.dumps(data))
        except websockets.exceptions.ConnectionClosed:
            del self.client_websocket_dict[client]

    async def unhandled_message(self, data):
        print(f"Unhandled message type: {data['type']}")

    async def save_evaluation_periodically(self):
        while True:
            await asyncio.sleep(self.evaluation_interval)            
            self.save_evaluation()

    def save_evaluation(self):
        # 評価データをJSON形式で保存
        with open("evaluation.json", "w") as f:
            print(self.evaluation)
            # 整形して保存
            json.dump(self.evaluation, f, default=str, indent=4)

server = WebSocketServer()
asyncio.run(server.start_server())