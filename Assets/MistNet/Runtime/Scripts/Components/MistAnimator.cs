using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using MemoryPack;
using Newtonsoft.Json;
using UnityEngine;

namespace MistNet
{
    [RequireComponent(typeof(MistSyncObject))]
    public class MistAnimator : MonoBehaviour
    {
        private static readonly float UpdateIntervalTimeSec = 0.5f;
        private readonly CancellationTokenSource _tokenSource = new();
        public Animator Animator { get; set; }

        [SerializeField] private AnimatorState[] animatorState = new[]
        {
            new AnimatorState { StateName = "Speed", Type = AnimatorControllerParameterType.Float },
            new AnimatorState { StateName = "MotionSpeed", Type = AnimatorControllerParameterType.Float },
            new AnimatorState { StateName = "Jump", Type = AnimatorControllerParameterType.Bool },
        };

        [Serializable]
        public class AnimatorState
        {
            public string StateName;
            public AnimatorControllerParameterType Type;
            public int Hash => _hash != 0 ? _hash : _hash = Animator.StringToHash(StateName);
            public bool BoolValue { get; set; }
            public float FloatValue { get; set; }

            public bool TriggerValue
            {
                get
                {
                    if (_triggerValue)
                    {
                        _triggerValue = false;
                        return true;
                    }

                    return false;
                }
                set => _triggerValue = value;
            }

            public int IntValue { get; set; }
            private bool _triggerValue;
            private int _hash;
        }

        private MistSyncObject _syncObject;
        private readonly Dictionary<string, int> _animStateHash = new();

        private void Start()
        {
            _syncObject = GetComponent<MistSyncObject>();
            foreach (var state in animatorState)
            {
                _animStateHash.Add(state.StateName, state.Hash);
            }

            if (!_syncObject.IsOwner) return;
            UpdateAnim(_tokenSource.Token).Forget();
        }

        private void OnDestroy()
        {
            _tokenSource.Cancel();
        }

        private async UniTask UpdateAnim(CancellationToken token)
        {
            if (!_syncObject.IsOwner) return;
            while (!token.IsCancellationRequested)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(UpdateIntervalTimeSec), cancellationToken: token);
                if (Animator == null) continue;

                GetAnimatorState();
                SendAnimState();
            }
        }

        private void SendAnimState()
        {
            var stateText = JsonConvert.SerializeObject(animatorState);
            var sendData = new P_Animation
            {
                ObjId = _syncObject.Id,
                State = stateText,
            };
            var bytes = MemoryPackSerializer.Serialize(sendData);
            MistManager.I.SendAll(MistNetMessageType.Animation, bytes);
        }

        public void ReceiveAnimState(P_Animation receiveData)
        {
            animatorState = JsonConvert.DeserializeObject<AnimatorState[]>(receiveData.State);
            SetAnimatorState();
        }

        private void SetAnimatorState()
        {
            if (Animator == null) return;
            foreach (var state in animatorState)
            {
                var stateHash = _animStateHash[state.StateName];
                switch (state.Type)
                {
                    case AnimatorControllerParameterType.Bool:
                        Animator.SetBool(stateHash, state.BoolValue);
                        break;
                    case AnimatorControllerParameterType.Float:
                        Animator.SetFloat(stateHash, state.FloatValue);
                        break;
                    case AnimatorControllerParameterType.Int:
                        Animator.SetInteger(stateHash, state.IntValue);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        if (state.TriggerValue)
                        {
                            Animator.SetTrigger(stateHash);
                        }
                        break;
                }
            }
        }

        private void GetAnimatorState()
        {
            foreach (var state in animatorState)
            {
                switch (state.Type)
                {
                    case AnimatorControllerParameterType.Bool:
                        state.BoolValue = Animator.GetBool(state.Hash);
                        break;
                    case AnimatorControllerParameterType.Float:
                        state.FloatValue = Animator.GetFloat(state.Hash);
                        break;
                    case AnimatorControllerParameterType.Int:
                        state.IntValue = Animator.GetInteger(state.Hash);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        state.TriggerValue = Animator.GetBool(state.Hash);
                        break;
                }
            }
        }
    }
}