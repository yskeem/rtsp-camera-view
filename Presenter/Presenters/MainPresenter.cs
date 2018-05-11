﻿using System;
using Model;
using Model.Rtsp;
using Presenter.Common;
using Presenter.Views;
using Presenter.Presenters;
using Microsoft.Win32;

/* TODO:

Локализация на русский.

Переподключаться при плохом сигнале.

Отслеживание превышения памяти.

Отправлять почту, когда не удается получить картинку очень долго.
*/

namespace Presenter.Presenters
{
    public class MainPresenter : BasePresener<IMainView>
    {
        private SourceListPresenter _sourceList = null;
        private SourceGridPresenter _sourceGrid = null;
        private ModifySettingsPresenter _settings = null;
        private AppSettings _appSettings = null;
        private ISettingsService _settingsService = null;

        public MainPresenter(IApplicationController controller, IMainView view, ISettingsService settingsService)
            : base(controller, view)
        {
            // View actions
            View.OpenClosePanelClick += OpenClosePanelClick;
            View.SourcesPageSelected += SourcesPageSelected;
            View.SettingsPageSelected += SettingsPageSelected; 
            View.SplitterMoved += SplitterMoved;

            // Settings
            _settingsService = settingsService;
            try { _appSettings = settingsService.GetSettings(); }
            catch (UnauthorizedAccessException e) { View.ErrorAccessSettings(e.Message); }
            catch (Exception e) { View.ErrorOnLoadSettings(e.Message); }
            if (_appSettings == null) _appSettings = settingsService.GetSettings();
            if (_appSettings.cams.Length == 0) if (View.AskIfAddSamples()) settingsService.AddSampleCameras();

            View.CtrlPanelWidth = _appSettings.controlPanelWidth;
            // Sources grid
            try { CreateGrid(); }
            catch (System.IO.FileNotFoundException e) { View.ErrorOnCreateGridNoLib(e.Message); } //no VLC or ActiveX lib
            catch (System.NotSupportedException e) { View.ErrorOnCreateGridBadVer(e.Message); } //bad VLC version
            catch (System.InvalidCastException e) { View.ErrorOnCreateGridBadVer(e.Message); } //bad VLC version
            catch (Exception e) { View.ErrorOnCreateGridOther(e.Message); }
            foreach (Camera m in _appSettings.cams) { m.Edit += () => EditSrcClick(m); }
            // Process command line arguments
            ProcessCommandLine(Environment.GetCommandLineArgs());
            // Prepare hints engine
            _appSettings.hint.onShow += ShowHintWithWait;
            _appSettings.hint.onHide += HideHint;
            View.HideHintTimer += () => _appSettings.hint.Hide();
            View.ShowHintTimer += ShowHint;
            // Hint if empty grid and control is not shown
            if (!View.PanelState)
            {
                bool camPosFound = false;
                foreach (Camera c in _appSettings.cams) if (c.position >= 0) camPosFound = true;
                if(!camPosFound) _appSettings.hint.Show(HintType.OpenCtrl);
            }
        }

        private void ProcessCommandLine(string[] args)
        {
            int screen = _appSettings.screen, fullscreen = _appSettings.fullscreen, autoplay = _appSettings.autoplay;
            int unmute = _appSettings.unmute, maximize = _appSettings.maximize, priority = _appSettings.priority;
            bool f;
            foreach (string arg in args)
            {
                int l = arg.Length;
                if (l >= 10) if (arg.Substring(0, 10).Equals("fullscreen")) if (l > 10) f = int.TryParse(arg.Substring(11), out fullscreen); else fullscreen = 1;
                if (l >= 8)
                {
                    string s = arg.Substring(0, 8);
                    if (s.Equals("autoplay"))      { if (l > 8) f = int.TryParse(arg.Substring(9), out autoplay); else autoplay = 1; }
                    else if (s.Equals("priority")) { if (l > 8) f = int.TryParse(arg.Substring(9), out priority); }
                    else if (s.Equals("maximize")) { if (l > 8) f = int.TryParse(arg.Substring(9), out maximize); else maximize = 1; }
                }
                if (l >= 6)
                {
                    string s = arg.Substring(0, 6);
                    if (s.Equals("unmute")) if (l > 6) f = int.TryParse(arg.Substring(7), out unmute); else unmute = 1;
                    if (s.Equals("screen")) if (l > 6) f = int.TryParse(arg.Substring(7), out screen);
                }
            }
            _appSettings.unmute_now = unmute;
            if (screen > -1) View.MoveToScreen(screen);
            if (maximize > 0) View.Maximize();
            if (fullscreen > 0) View.DoFullscreen();
            if (autoplay > 0) { _appSettings.autoplay_now = autoplay; _sourceGrid.PlayAll(); }
            if (priority >= 0) View.SetPriority(priority);
            View.SetAppCaption();
        }

        private void SaveSettings()
        {
            try { _settingsService.Save(); }
            catch (UnauthorizedAccessException e) { View.ErrorAccessSettings(e.Message); }
            catch (Exception e) { View.ErrorOnSaveSettings(e.Message); }
        }

        private void OpenClosePanelClick()
        {
            View.PanelState = !View.PanelState;
        }
        private void SplitterMoved()
        {
            _appSettings.controlPanelWidth = View.CtrlPanelWidth;
        }
        private void SourcesPageSelected()
        {
            _appSettings.hint.Hide();
            if (_sourceList == null)
            {
                _sourceList = Controller.Get<SourceListPresenter>();
                _sourceList.SetSettings(_appSettings);
                _sourceList.SourceCreated += SourceCreated;
                _sourceList.SourceModified += SourceModified;
                _sourceList.SourceDeleted += SourceDeleted;
                _sourceList.DoDragDrop += SourceDoDragDrop;
                _sourceList.DoneDragDrop += SourceDoneDragDrop;
                View.SetSourceListControl(_sourceList.Control);
            }
            if (_appSettings.cams.Length == 0) _appSettings.hint.Show(HintType.AddCamera);
        }
        private void SettingsPageSelected()
        {
            _appSettings.hint.Hide();
            if (_settings == null)
            {
                _settings = Controller.Get<ModifySettingsPresenter>();
                _settings.SetSettings(_appSettings);
                View.SetSettingsControl(_settings.Control);
                _settings.NameViewChanged += NameViewChanged;
                _settings.MatrixSizeChanged += MatrixSizeChanged;
            }
        }
        private void CreateGrid()
        {
            if (_sourceGrid == null)
            {
                _sourceGrid = Controller.Get<SourceGridPresenter>();
                _sourceGrid.SetSettings(_appSettings);
                View.SetGridControl(_sourceGrid.Control);
            }
        }
        private void EditSrcClick(Camera s)
        {
            View.PanelState = true;
            _sourceList.EditClick(s);
        }
        private void SourceCreated()
        {
            Camera c = _sourceList.SourceEditedVar;
            c.Edit += () => EditSrcClick(c);
            SaveSettings();
        }
        private void SourceModified()
        {
            _sourceGrid.SourceRefreshed(_sourceList.SourceEditedVar);
            SaveSettings();
        }
        private void SourceDeleted()
        {
            _sourceGrid.SourceDeleted(_sourceList.SourceDeletedVar);
        }
        private void SourceDoDragDrop()
        {
            _sourceGrid.SourceDoDragDrop((Camera)_sourceList.DragObject);
        }
        private void SourceDoneDragDrop()
        {
            _sourceGrid.SourceDoneDragDrop();
        }
        private void NameViewChanged()
        {
            _sourceGrid.GlobalNameViewChanged();
        }
        private void MatrixSizeChanged()
        {
            _sourceGrid.SetSettings(_appSettings);
        }

        private void ShowHintWithWait()
        {
            View.HintShowTimer = true;
        }
        private void ShowHint()
        {
            switch (_appSettings.hint.lastType) {
                case HintType.OpenCtrl: View.HintOpenCtrlShow = true; break;
                case HintType.AddCamera: View.HintAddCameraShow = true; break;
                case HintType.DropCamera: View.HintDropCameraShow = true; break;
                case HintType.NewRtspBad: View.HintNewRtspBadShow = true; break;
                case HintType.NewRtspGood: View.HintNewRtspGoodShow = true; break;
                case HintType.None: break;
            }
        }
        private void HideHint()
        {
            View.HintShowTimer = false;
            switch (_appSettings.hint.lastType)
            {
                case HintType.OpenCtrl: View.HintOpenCtrlShow = false; break;
                case HintType.AddCamera: View.HintAddCameraShow = false; break;
                case HintType.DropCamera: View.HintDropCameraShow = false; break;
                case HintType.NewRtspBad: View.HintNewRtspBadShow = false; break;
                case HintType.NewRtspGood: View.HintNewRtspGoodShow = false; break;
                case HintType.None: break;
            }
        }

    }
}