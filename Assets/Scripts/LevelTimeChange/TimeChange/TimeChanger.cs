using System;
using UnityEngine;
using System.Collections.Generic;
using CoinPackage.Debugging;
using CustomInput;
using DataPersistence;
using UnityEngine.InputSystem;
using Settings;
using UI;
using Cinemachine;
using InteractableObjectSystem;

namespace LevelTimeChange.TimeChange {
    /// <summary>
    /// Manages time changing mechanic.
    /// </summary>
    public class TimeChanger : MonoBehaviour, IDataPersistence {
        public static TimeChanger Instance { get; private set; }
        public bool SceneObject { get; } = false;

        public event EventHandler<OnTimeChangeEventArgs> OnTimeChange;
        public event EventHandler<OnTimeChangeEventArgs> OnTryTimeChange;
        public event EventHandler OnTeleportationEnded;
        public event EventHandler TimeChangeUnlocked;

        public class OnTimeChangeEventArgs : EventArgs {
            public TimeLine previousTime;
            public TimeLine time;
        }

        [SerializeField] private Animator animator;

        GameObject _timeChangeUIgo;
        bool _timeChangeActivated = false;
        bool _hasCzasoport = false;
        bool _hasCzasoportPart = false;

        /// <summary>
        /// Timeline the player is currently on.
        /// </summary>
        public TimeLine actualTime = TimeLine.Present;

        private List<CheckCollider> _boxes;
        private TimePlatformChangeSettingsSO _settings;
        private Vector3 _timeJump;
        private TimeLine _newTimeLine;
        [SerializeField]
        private List<bool> _timeUnlocked = new List<bool>() { false, false, false };

        [SerializeField] private CinemachineVirtualCamera _camera;

        public Animator Animator => animator;

        private void Awake() {
            Instance = this;
            _timeChangeUIgo = FindObjectOfType<TimeChangeUI>().gameObject;

            _settings = DeveloperSettings.Instance.tpcSettings;
            _timeJump = _settings.offsetFromPresentPlatform;

            _boxes = new List<CheckCollider>();
            for (int i = -2; i <= 2; i++) {
                if (i == 0) {
                    _boxes.Add(null);
                    continue;
                }

                GameObject objectToSpawn = new GameObject("ColliderToSpawn");
                objectToSpawn.transform.parent = transform;
                objectToSpawn.AddComponent<CircleCollider2D>();

                objectToSpawn.transform.position = i * _timeJump + transform.position;
                _boxes.Add(objectToSpawn.AddComponent<CheckCollider>());
            }


            _timeChangeUIgo.SetActive(false);
        }

        private void OnEnable() {
            CInput.InputActions.Teleport.TeleportBack.performed += TimeBack;
            CInput.InputActions.Teleport.TeleportForward.performed += TimeForward;
        }

        private void OnDisable() {
            CInput.InputActions.Teleport.TeleportBack.performed -= TimeBack;
            CInput.InputActions.Teleport.TeleportForward.performed -= TimeForward;
        }

        private void TimeBack(InputAction.CallbackContext ctx) {
            TryChange(-1);

        }

        private void TimeForward(InputAction.CallbackContext ctx) {
            TryChange(1);
        }

        /// <summary>
        /// Managing time changing mechanic. If Player can change time, calls ChangeTime().
        /// </summary>
        /// <param name="change">-1 to go back in TimeLine or 1 to go forward in TimeLine.</param>
        private void TryChange(int change) {
            if (_settings.loopTimeChange) {
                if (actualTime == 0 && change == -1) change = 2;
                _newTimeLine = (TimeLine)(((int)actualTime + change) % 3);
            }
            else {
                if ((actualTime == TimeLine.Future && change == 1) || (actualTime == TimeLine.Past && change == -1)) return;
                _newTimeLine = actualTime + change;
            }
            if (CanChangeTime(_newTimeLine)) {
                OnTimeChange?.Invoke(this, new OnTimeChangeEventArgs {
                    previousTime = actualTime,
                    time = _newTimeLine
                });
                StartCoroutine(ChangeTime());
            }
            else {
                OnTryTimeChange?.Invoke(this, new OnTimeChangeEventArgs {
                    previousTime = actualTime,
                    time = _newTimeLine
                });
            }
        }

        /// <summary>
        /// Changes time to _newTimeline
        /// </summary>
        private IEnumerator<WaitForSeconds> ChangeTime() {
            var key = CInput.TeleportLock.Lock();
            animator.SetTrigger("Start");
            yield return new WaitForSeconds(_settings.timelineChangeAnimLength / 2);
            CinemachineFramingTransposer cinemachineFramingTransposer = _camera.GetCinemachineComponent<CinemachineFramingTransposer>();
            Vector2 softZones = new Vector2(cinemachineFramingTransposer.m_SoftZoneWidth, cinemachineFramingTransposer.m_SoftZoneHeight);
            cinemachineFramingTransposer.m_SoftZoneHeight = 0f;
            cinemachineFramingTransposer.m_SoftZoneWidth = 0f;
            transform.Translate(_timeJump * (int)(_newTimeLine - actualTime));
            actualTime = _newTimeLine;
            animator.SetTrigger("End");
            yield return new WaitForSeconds(_settings.timelineChangeAnimLength / 2);
            cinemachineFramingTransposer.m_SoftZoneWidth = softZones.x;
            cinemachineFramingTransposer.m_SoftZoneHeight = softZones.y;
            CInput.TeleportLock.Unlock(key);
            OnTeleportationEnded?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Asks appropriate CheckCollider if Player can change time.
        /// </summary>
        public bool CanChangeTime(TimeLine timeToCheck) {
            return _boxes[(int)timeToCheck - (int)actualTime + 2].IsNotTouching() && _timeUnlocked[(int)timeToCheck];
        }

        public void LoadPersistentData(GameData gameData) {
            actualTime = gameData.currentTimeline;
            _timeUnlocked[(int)actualTime] = true;
            if (_timeChangeActivated) _timeChangeUIgo.GetComponent<TimeChangeUI>().UpdateTimeUI();

            if (gameData.playerGameData.hasCzasoport)
                PickUpCzasoport();
            if (gameData.playerGameData.hasCzasoportPart)
                PickUpCzasoportPart();
        }

        public void SavePersistentData(ref GameData gameData) {
            gameData.currentTimeline = actualTime;
            gameData.playerGameData.hasCzasoport = _hasCzasoport;
            gameData.playerGameData.hasCzasoportPart = _hasCzasoportPart;
        }

        public void UnlockTimeline(TimeLine timelineToUnlock) {
            _timeUnlocked[(int)timelineToUnlock] = true;
            _timeChangeUIgo.GetComponent<TimeChangeUI>().ChangeInvBackground(timelineToUnlock);
            TimeChangeUnlocked?.Invoke(this, EventArgs.Empty);
        }

        public void LockTimeline(TimeLine timelineToLock) {
            _timeUnlocked[(int)timelineToLock] = false;
        }

        public bool IsTimeUnlocked(TimeLine timeLine) {
            return _timeUnlocked[(int)timeLine];
        }

        public void EnableTimeChange() {
            _timeChangeUIgo.GetComponent<TimeChangeUI>().UnlockTimeUI();
            _timeChangeUIgo.SetActive(true);
        }

        public void PickUpCzasoport() {
            _hasCzasoport = true;
            EnableTimeChange();
            UnlockTimeline(TimeLine.Present);
            UnlockTimeline(TimeLine.Future);
            _timeChangeUIgo.GetComponent<TimeChangeUI>().UpdateTimeUI();
        }

        public void PickUpCzasoportPart(InterObjectDefinition definition = null) {
            _hasCzasoportPart = true;
            if (!IsTimeUnlocked(TimeLine.Present)) {
                if (definition != null)
                    NotificationManager.Instance.RaiseNotification(definition.failedHandInterNotification);
                return;
            }
            UnlockTimeline(TimeLine.Past);
            if (definition != null)
                NotificationManager.Instance.RaiseNotification(definition.successfulHandInterNotification);
            FindObjectOfType<TimeChangeUI>().UpdateTimeUI();
        }

        public override string ToString() {
            return $"[TimeChange]" % Colorize.Gold;
        }
    }
}