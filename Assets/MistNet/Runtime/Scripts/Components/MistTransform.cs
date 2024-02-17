using UnityEngine;

namespace MistNet
{
    [RequireComponent(typeof(MistSyncObject))]
    public class MistTransform : MonoBehaviour
    {
        private MistSyncObject _syncObject;
        private float _time;
        private float _syncIntervalTimeSecond = 0.1f;
        private P_Location _sendData;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        private Vector3 _receivedPosition = Vector3.zero;
        private Quaternion _receivedRotation = Quaternion.identity;
        private float _elapsedTime;

        private void Start()
        {
            _syncObject = GetComponent<MistSyncObject>();

            if (!_syncObject.IsOwner)
            {
                _syncIntervalTimeSecond = 0; // まだ受信していないので、同期しない
                return;
            }

            _sendData = new()
            {
                ObjId = _syncObject.Id,
                Time = _syncIntervalTimeSecond
            };
        }

        private void Update()
        {
            if (_syncObject.IsOwner)
            {
                UpdateAndSendLocation();
            }
            else
            {
                InterpolationLocation();
            }
        }

        public void ReceiveLocation(P_Location location)
        {
            if (_syncObject == null) return;
            if (_syncObject.IsOwner) return;

            _receivedPosition = location.Position;
            _receivedRotation = Quaternion.Euler(location.Rotation);
            _syncIntervalTimeSecond = location.Time;
            Debug.Log($"[{location.ObjId}] Time: {_syncIntervalTimeSecond}");
            _elapsedTime = 0f;
        }

        private void UpdateAndSendLocation()
        {
            _time += Time.deltaTime;
            if (_time < _syncIntervalTimeSecond) return;
            _time = 0;

            // 座標が変わっていない場合は、送信しない
            if (_previousPosition == transform.position &&
                _previousRotation == transform.rotation) return;

            // 座標が異なる場合、送信する
            _previousPosition = transform.position;
            _previousRotation = transform.rotation;

            _sendData.Position = transform.position;
            _sendData.Rotation = transform.rotation.eulerAngles;
            
            // var bytes = MemoryPackSerializer.Serialize(_sendData);
            // MistManager.I.SendAll(MistNetMessageType.Location, bytes);
            MistSendingOptimizer.I.SendLocationData = _sendData;
        }

        private void InterpolationLocation()
        {
            if (_syncIntervalTimeSecond == 0) return;
            
            var time = _elapsedTime / _syncIntervalTimeSecond;
            _elapsedTime += Time.deltaTime;
            transform.position = Vector3.Lerp(transform.position, _receivedPosition, time);
            transform.rotation = Quaternion.Lerp(transform.rotation, _receivedRotation, time);
        }
    }
}