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
        private readonly CancellationTokenSource _tokenSource = new ();
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
        private Dictionary<string, int> _animStateHash = new ();
        private Dictionary<string, AnimatorState> _animStateDict = new ();

        private void Awake()
        {
            _syncObject = GetComponent<MistSyncObject>();
            foreach (var state in animatorState)
            {
                _animStateHash.Add(state.StateName, state.Hash);
            }
            foreach (var state in animatorState)
            {
                _animStateDict.Add(state.StateName, state);
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
            var states = JsonConvert.DeserializeObject<AnimatorState[]>(receiveData.State);
            foreach (var state in states)
            {
                _animStateDict[state.StateName] = state;
            }

            SetAnimatorState();
        }

        private void SetAnimatorState()
        {
            foreach (var state in _animStateDict.Values)
            {
                switch (state.Type)
                {
                    case AnimatorControllerParameterType.Bool:
                        Animator.SetBool(state.Hash, _animStateDict[state.StateName].BoolValue);
                        break;
                    case AnimatorControllerParameterType.Float:
                        Animator.SetFloat(state.Hash, _animStateDict[state.StateName].FloatValue);
                        break;
                    case AnimatorControllerParameterType.Int:
                        Animator.SetInteger(state.Hash, _animStateDict[state.StateName].IntValue);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        if (_animStateDict[state.StateName].TriggerValue)
                        {
                            Animator.SetTrigger(state.Hash);
                        }
                        break;
                }
            }
        }
        
        private void GetAnimatorState()
        {
            foreach (var state in _animStateDict.Values)
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