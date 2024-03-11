using MemoryPack;
using UnityEngine;

namespace MistNet
{
    [RequireComponent(typeof(MistSyncObject))]
    public class MistTransform : MonoBehaviour
    {
        [Tooltip("Note: This setting is applicable to all synchronized objects, excluding a player object")]
        [SerializeField]
        private float _initialSyncIntervalTimeSecond = 0.1f;

        [SerializeField] private float _maxSyncIntervalTimeSecond = 0.5f;
        [SerializeField] private float _minSyncIntervalTimeSecond = 0.05f;
        [SerializeField] private float _predictionDuration = 0.2f;

        private MistSyncObject _syncObject;
        private float _time;
        private P_Location _sendData;
        private Vector3 _previousPosition;
        private Quaternion _previousRotation;
        private Vector3 _receivedPosition = Vector3.zero;
        private Quaternion _receivedRotation = Quaternion.identity;
        private float _elapsedTime;
        private float _syncIntervalTimeSecond;

        private void Start()
        {
            _syncObject = GetComponent<MistSyncObject>();
            _syncIntervalTimeSecond = _initialSyncIntervalTimeSecond;

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
                InterpolateLocation();
            }
        }

        private void UpdateAndSendLocation()
        {
            _time += Time.deltaTime;
            if (_time < _syncIntervalTimeSecond) return;
            _time = 0;

            // 座標が変わっていない場合は、送信間隔を増やす
            if (_previousPosition == transform.position &&
                _previousRotation == transform.rotation)
            {
                _syncIntervalTimeSecond = Mathf.Min(_syncIntervalTimeSecond * 1.1f, _maxSyncIntervalTimeSecond);
                return;
            }

            // 座標が異なる場合、送信間隔を減らす
            _syncIntervalTimeSecond = Mathf.Max(_syncIntervalTimeSecond * 0.9f, _minSyncIntervalTimeSecond);

            // velocityの計算
            Vector3 velocity = -(transform.position - _previousPosition) / _syncIntervalTimeSecond;

            // 座標情報の更新
            _previousPosition = transform.position;
            _previousRotation = transform.rotation;

            _sendData.Position = transform.position;
            _sendData.Rotation = transform.rotation.eulerAngles;
            _sendData.Velocity = velocity;

            if (_syncObject.IsPlayerObject && MistSendingOptimizer.I != null)
            {
                MistSendingOptimizer.I.SendLocationData = _sendData;
                return;
            }

            _sendData.Time = _syncIntervalTimeSecond;
            var bytes = MemoryPackSerializer.Serialize(_sendData);
            MistManager.I.SendAll(MistNetMessageType.Location, bytes);
        }

        private Vector3 _startPosition;
        private Vector3 _targetPosition;
        private Vector3 _startRotation;
        private Vector3 _targetRotation;
        private Vector3 _receivedVelocity;
        private bool _isInterpolating;

        private void InterpolateLocation()
        {
            if (_syncIntervalTimeSecond == 0) return;

            if (!_isInterpolating)
            {
                // 新しい位置を受信したら、補間を開始する
                _startPosition = transform.position;
                _targetPosition = _receivedPosition + _receivedVelocity * _predictionDuration;
                _startRotation = transform.rotation.eulerAngles;
                _targetRotation = _receivedRotation.eulerAngles;
                _isInterpolating = true;
                _elapsedTime = 0f;
            }

            if (_isInterpolating)
            {
                // 速度を考慮した補間
                float t = Mathf.Clamp01(_elapsedTime / _syncIntervalTimeSecond);
                float smoothT = t * t * (3f - 2f * t); // 3次のイーズイン・イーズアウト補間

                Vector3 currentPosition = Vector3.Lerp(_startPosition, _targetPosition, smoothT);
                Vector3 currentRotation = Vector3.Lerp(_startRotation, _targetRotation, smoothT);

                transform.position = currentPosition;
                transform.rotation = Quaternion.Euler(currentRotation);

                _elapsedTime += Time.deltaTime;

                if (_elapsedTime >= _syncIntervalTimeSecond)
                {
                    // 補間が完了したら、補間を停止する
                    _isInterpolating = false;
                }
            }
        }

        public void ReceiveLocation(P_Location location)
        {
            if (_syncObject == null) return;
            if (_syncObject.IsOwner) return;

            _receivedPosition = location.Position;
            _receivedRotation = Quaternion.Euler(location.Rotation);
            _receivedVelocity = location.Velocity;
            _syncIntervalTimeSecond = location.Time;
            MistDebug.Log($"[{location.ObjId}] Time: {_syncIntervalTimeSecond}");
        }
    }
}