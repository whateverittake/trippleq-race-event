using UnityEngine;
using static TrippleQ.Event.RaceEvent.Runtime.PopupTypes;

namespace TrippleQ.Event.RaceEvent.Runtime
{
    public sealed class RaceEventUIController : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private RaceEventBootstrap _bootstrap;

        [Header("Views")]
        [SerializeField] private RaceSearchingPopupView _searchingView;
        [SerializeField] private RaceEndPopupView _endedView;
        [SerializeField] private RaceMainPopupView _mainPopupView;
        [SerializeField] private RaceEntryPopupView _entryView;
        [SerializeField] private RaceEventHudWidgetView _hudWidgetView;
        [SerializeField] RaceInfoPopupView _infoPopupView;

        private RaceEventService _svc;

        private RaceEventHudWidgetPresenter _hudPresenter;

        private RaceEntryPopupPresenter _entryPresenter;
        private RaceMainPopupPresenter _mainPresenter;
        private RaceSearchingPopupPresenter _searchingPresenter;
        private RaceEndPopupPresenter _endPresenter;
        private RaceInfoPopupPresenter _infoPresenter;

        private void Awake()
        {
            // nếu bootstrap Awake chạy trước thì Service đã có sẵn
            if (_bootstrap != null && _bootstrap.Service != null)
            {
                Bind(_bootstrap.Service);
                return;
            }

            // fallback: chờ signal ready
            if (_bootstrap != null) _bootstrap.OnServiceReady += Bind;
            else Debug.LogError("Missing bootstrap ref");
        }

        private void OnDestroy()
        {
            if (_bootstrap != null)
                _bootstrap.OnServiceReady -= Bind;

            Unbind();
        }

        private void Update()
        {
            _hudPresenter?.Tick(Time.deltaTime);

            _mainPresenter?.Tick(Time.deltaTime);
            _infoPresenter?.Tick(Time.deltaTime);
        }

        public void InitBootstrap(RaceEventBootstrap bootstrap)
        {
            _bootstrap = bootstrap;

            // nếu bootstrap Awake chạy trước thì Service đã có sẵn
            if (_bootstrap != null && _bootstrap.Service != null)
            {
                Bind(_bootstrap.Service);
                return;
            }

            // fallback: chờ signal ready
            if (_bootstrap != null) _bootstrap.OnServiceReady += Bind;
            else Debug.LogError("Missing bootstrap ref");
        }

        private void Bind(RaceEventService svc)
        {
            if (_svc == svc) return;

            Unbind();
            _svc = svc;

            // --- events ---
            _svc.OnPopupRequested += HandlePopup;

            bool isInTutorial() => false; // TODO: hook tutorial check
            // HUD bind
            _hudPresenter = new RaceEventHudWidgetPresenter(_svc, _hudWidgetView, isInTutorial);

            _entryPresenter = new RaceEntryPopupPresenter(_svc, isInTutorial);
            _searchingPresenter= new RaceSearchingPopupPresenter(_svc);
            _mainPresenter = new RaceMainPopupPresenter(_svc, isInTutorial);
            _endPresenter= new RaceEndPopupPresenter(_svc);
            _infoPresenter= new RaceInfoPopupPresenter(_svc);

            // --- initial bind snapshot ---
            //ReplaySnapshot();
        }

        private void Unbind()
        {
            // đóng và unbind tất cả presenters (quan trọng)
            HideAll();
            _entryPresenter = null;
            _mainPresenter = null;
            _searchingPresenter = null;
            _endPresenter = null;

            _hudPresenter?.Dispose();
            _hudPresenter = null;

            if (_svc == null) return;
            _svc.OnPopupRequested -= HandlePopup;
            _svc = null;
        }

        private void HandlePopup(PopupRequest req)
        {
            Debug.Log("HandlePopup: " + req.Type);
            // routing duy nhất của controller
            switch (req.Type)
            {
                case PopupType.Entry:
                    ShowEntry();
                    break;
                case PopupType.Searching:
                    ShowSearching(req);
                    break;
                case PopupType.Main:
                    ShowMain();
                    break;
                case PopupType.Ended:
                    ShowEnd();
                    break;
                case PopupType.Info:
                    ShowInfo();
                    break;
                default:
                    Debug.LogWarning("Unhandled popup type: " + req.Type);
                    break;
            }
        }

        // ---------------- UI actions / show hide ----------------

        private void ReplaySnapshot()
        {
            if (_svc == null) return;

            switch (_svc.State)
            {
                case RaceEventState.InRace:
                    ShowMain();
                    break;

                case RaceEventState.Searching:
                    // nếu Searching cần data req thì:
                    // ShowSearching(new PopupRequest { Type=PopupType.Searching, Searching = _svc.GetSearchingSnapshot() });
                    // còn không thì HideAll() hoặc giữ trạng thái hiện tại
                    //HideAll();
                    //var plan = _svc.GetSearchingSnapshot(); // thêm hàm này ở service (bên dưới)
                    //ShowSearching(new PopupRequest { PopupType.Searching, Searching = plan });
                    break;

                case RaceEventState.Ended:
                case RaceEventState.ExtendOffer:
                    ShowEnd();
                    break;

                case RaceEventState.Eligible:
                    ShowEntry();
                    break;

                default:
                    Debug.Log("OnClickIconRace: not state can show: " + _svc.State);
                    HideAll();
                    break;
            }
        }

        private void HideAll()
        {
            _entryPresenter?.Hide();
            _entryPresenter?.Unbind();

            _mainPresenter?.Hide();
            _mainPresenter?.Unbind();

            _searchingPresenter?.Hide();
            _searchingPresenter?.Unbind();

            _endPresenter?.Hide();
            _endPresenter?.Unbind();

            _infoPresenter?.Hide();
            _infoPresenter?.Unbind();
        }

        private void ShowInfo()
        {
            HideAll();
            var v = (IRaceInfoPopupView)_infoPopupView;
            _infoPresenter.Bind(v);
            _infoPresenter.Show();
        }

        private void ShowEntry()
        {
            HideAll();
            var v = (IRaceEntryPopupView)_entryView;
            _entryPresenter.Bind(v);
            _entryPresenter.Show();
        }

        private void ShowSearching(PopupRequest req)
        {
            HideAll();

            _searchingPresenter.SetPlan(req.Searching);

            var v = (IRaceSearchingPopupView)_searchingView;
            _searchingPresenter.Bind(v);
            _searchingPresenter.Show();
        }

        private void ShowMain()
        {
            HideAll();
            var v = (IRaceMainPopupView)_mainPopupView;
            _mainPresenter.Bind(v);
            _mainPresenter.Show();
        }

        private void ShowEnd()
        {
            HideAll();
            var v = (IRaceEndPopupView)_endedView;
            _endPresenter.Bind(v);
            _endPresenter.Show();
        }

    }
}
