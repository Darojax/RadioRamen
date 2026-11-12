using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Managers___Handlers;
using RSServiceLocator.Scripts;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace RadioRamen;

// Browsing belongs to the game session, independently of the disposable menu.
internal sealed class RadioBrowseState
{
    internal bool Music, FavouritesOnly;
    internal string Category = "All categories", Region = "All regions";
    internal readonly LibraryBrowseState Radio = new(), Tracks = new();
    internal LibraryBrowseState View(bool music) => music ? Tracks : Radio;
}

internal sealed class LibraryBrowseState
{
    internal string Search = "";
    internal Vector2 Scroll;
}

internal sealed class RadioPanel : IDisposable
{
    private static readonly Color Paper = Hex(0xEBE8D1), Inset = Hex(0xE1DAC2), Ink = Hex(0x604B39), MutedInk = Hex(0x78634B);
    private static readonly Color Wood = Hex(0xB38A50), WoodHover = Hex(0xA47943), Blue = Hex(0x478AAC), Red = Hex(0xA44157);
    private readonly Plugin _owner;
    private readonly GameObject _object;
    private readonly PanelSettings _settings;
    private readonly VisualElement _root, _body, _main, _library, _filters, _musicControls;
    private readonly ScrollView _list, _player;
    private readonly Label _count, _title, _status, _position, _notice, _volumeValue;
    private readonly Button _radioTab, _musicTab, _categoryButton, _regionButton, _shuffle, _repeat, _mute;
    private readonly Button _favouritesButton, _addStationButton;
    private readonly VisualElement _stationTools;
    private readonly Button[] _sound = new Button[3];
    private readonly Button[] _range = new Button[2];
    private readonly TextField _search;
    private readonly List<(Button button, Label name, Label marker, string source)> _items = new();
    private VisualElement? _picker;
    private VisualElement? _editor;
    private string _category = "All categories", _region = "All regions", _selected = "", _selectedMarker = "", _fontName = "Resolving notebook font";
    private FontDefinition _font;
    private bool _music, _hasFont, _disposed, _favouritesOnly;
    private bool _restorePending, _restoreQueued;
    private Vector2 _restoreOffset;

    internal RadioPanel(Plugin owner)
    {
        _owner = owner;
        _music = owner.Browsing.Music; _category = owner.Browsing.Category;
        _region = owner.Browsing.Region; _favouritesOnly = owner.Browsing.FavouritesOnly;
        var pause = PauseMenuManager.Instance;
        if (!pause || !pause.PauseMenuUIDoc.panelSettings) throw new InvalidOperationException("The game's menu theme is not ready.");
        var notebook = RSServices.Current.Get<NotebookService>();
        var template = AccessTools.Field(typeof(NotebookService), "m_panelSettings").GetValue(notebook) as PanelSettings;
        _settings = UnityEngine.Object.Instantiate(template ? template : pause.PauseMenuUIDoc.panelSettings);
        _settings.name = "RadioRamen panel settings";
        _settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
        _settings.referenceResolution = new Vector2Int(1280, 900);
        _settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight; _settings.match = 1;
        _settings.sortingOrder = Math.Max(_settings.sortingOrder, pause.PauseMenuUIDoc.panelSettings.sortingOrder) + 500;
        _object = new GameObject("RadioRamen menu", typeof(UIDocument));
        try
        {
            var doc = _object.GetComponent<UIDocument>(); doc.panelSettings = _settings;
            _root = doc.rootVisualElement;
            Fill(_root); _root.style.justifyContent = Justify.Center; _root.style.alignItems = Align.Center;
            _body = new VisualElement { name = "radioramen-paper" };
            _body.style.width = 1100; _body.style.maxWidth = Length.Percent(94); _body.style.height = Length.Percent(90);
            _body.style.paddingLeft = _body.style.paddingRight = 24; _body.style.paddingTop = _body.style.paddingBottom = 20;
            _body.style.backgroundColor = Paper; _body.style.color = Ink; _body.style.fontSize = 20;
            _body.style.overflow = Overflow.Hidden;
            Round(_body, 14); _root.Add(_body);
            _notice = Text("", 17);

            var header = Row(); header.style.marginBottom = 14;
            var brand = Text("Radio Controls", 30); brand.style.unityFontStyleAndWeight = FontStyle.Bold; brand.style.flexGrow = 1; header.Add(brand);
            var close = MakeButton("X", owner.ClosePanel, Red, Paper); close.style.width = 38; close.style.height = 38; close.style.minWidth = 38; Round(close, 20); header.Add(close); _body.Add(header);

            var tabs = Row(); tabs.style.marginBottom = 14;
            _radioTab = MakeButton("Internet radio", () => Tab(false)); _radioTab.style.width = 190;
            _musicTab = MakeButton("Your music", () => Tab(true)); _musicTab.style.width = 165; _musicTab.style.marginLeft = 8;
            tabs.Add(_radioTab); tabs.Add(_musicTab); tabs.Add(Spacer());
            tabs.Add(MakeButton("Refresh", () => { try { var offset = BrowseOffset; owner.RefreshLibraries(); Populate(offset); SetText(_notice, ""); } catch (Exception ex) { SetText(_notice, ex.Message); } }));
            _body.Add(tabs);

            _main = Row(); _main.style.alignItems = Align.Stretch; _main.style.flexGrow = 1; _main.style.flexShrink = 1; _main.style.flexBasis = 0; _main.style.minHeight = 0; _body.Add(_main);
            _library = new VisualElement(); _library.style.flexGrow = 1; _library.style.flexBasis = 0; _library.style.minWidth = 0; _library.style.marginRight = 18; _main.Add(_library);
            var searchRow = Row(); searchRow.style.marginBottom = 10;
            var searchLabel = Text("Search", 20); searchLabel.style.width = 72; searchRow.Add(searchLabel);
            _search = new TextField(); _search.style.flexGrow = 1; _search.style.minWidth = 0; SkinInput(_search);
            _search.SetValueWithoutNotify(owner.Browsing.View(_music).Search);
            _search.RegisterValueChangedCallback(_ => Populate()); searchRow.Add(_search); _library.Add(searchRow);
            _filters = Row(); _filters.style.marginBottom = 8;
            var category = new VisualElement(); category.style.flexGrow = 1; category.style.flexBasis = 0; category.style.minWidth = 0; category.style.marginRight = 10;
            category.Add(Text("Category", 17)); _categoryButton = MakeButton("", () => Picker(_categoryButton!, new[] { "All categories" }.Concat(owner.Stations.Select(s => s.Category).Distinct().OrderBy(s => s)), _category, value => { _category = value; Populate(); })); category.Add(_categoryButton); _filters.Add(category);
            var region = new VisualElement(); region.style.flexGrow = 1; region.style.flexBasis = 0; region.style.minWidth = 0;
            region.Add(Text("Region", 17)); _regionButton = MakeButton("", () => Picker(_regionButton!, new[] { "All regions" }.Concat(owner.Stations.Select(s => s.Region).Distinct().OrderBy(s => s)), _region, value => { _region = value; Populate(); })); region.Add(_regionButton); _filters.Add(region); _library.Add(_filters);
            var countRow = Row(); countRow.style.marginBottom = 8;
            _count = Text("", 17); _count.style.flexGrow = 1; countRow.Add(_count);
            _stationTools = Row();
            _favouritesButton = MakeButton("Favourites", () => { _favouritesOnly = !_favouritesOnly; Populate(); });
            _favouritesButton.style.fontSize = 18; _favouritesButton.style.marginRight = 8; _stationTools.Add(_favouritesButton);
            _addStationButton = MakeButton("Add station", () => EditStation(null)); _addStationButton.style.fontSize = 18; _stationTools.Add(_addStationButton);
            countRow.Add(_stationTools); _library.Add(countRow);
            _list = new ScrollView(ScrollViewMode.Vertical); _list.name = "radioramen-stations"; _list.style.flexGrow = 1; _list.style.flexShrink = 1; _list.style.flexBasis = 0; _list.style.minHeight = 0;
            _list.style.paddingLeft = _list.style.paddingRight = 12; _list.style.paddingTop = _list.style.paddingBottom = 10;
            _list.style.backgroundColor = Wood; Round(_list, 14); SkinScroll(_list, true); _library.Add(_list);
            _list.contentContainer.RegisterCallback<GeometryChangedEvent>(_ => QueueBrowseRestore());
            _list.contentViewport.RegisterCallback<GeometryChangedEvent>(_ => QueueBrowseRestore());
            // A delayed layout restore must never override fresh player input.
            _list.RegisterCallback<WheelEvent>(_ => _restorePending = false, TrickleDown.TrickleDown);
            _list.RegisterCallback<PointerDownEvent>(_ => _restorePending = false, TrickleDown.TrickleDown);
            _list.RegisterCallback<NavigationMoveEvent>(_ => _restorePending = false, TrickleDown.TrickleDown);

            _player = new ScrollView(ScrollViewMode.Vertical); _player.name = "radioramen-player"; _player.style.width = 310; _player.style.flexShrink = 0;
            _player.style.paddingLeft = _player.style.paddingRight = 18; _player.style.paddingTop = _player.style.paddingBottom = 12;
            _player.style.backgroundColor = Inset; Round(_player, 14); SkinScroll(_player, false); _main.Add(_player);
            var heading = Text("Now playing", 24); heading.style.color = Paper; heading.style.backgroundColor = Blue; heading.style.paddingLeft = 12;
            heading.style.paddingTop = heading.style.paddingBottom = 5; heading.style.marginBottom = 12; Round(heading, 5); _player.Add(heading);
            _title = Text("Choose a station", 23); _title.style.whiteSpace = WhiteSpace.Normal; _title.style.marginBottom = 5; _player.Add(_title);
            _status = Text("", 18); _status.style.whiteSpace = WhiteSpace.Normal; _status.style.marginBottom = 6; _player.Add(_status);
            _position = Text("", 20); _position.style.marginBottom = 8; _player.Add(_position);
            _player.Add(MakeButton("Play / Resume", () => owner.PlaySelected(_music), Blue, Paper));
            var power = MakeButton("Power off", owner.PowerOff); power.style.marginTop = 6; _player.Add(power);
            _musicControls = new VisualElement(); _musicControls.style.marginTop = 6;
            _musicControls.Add(MakeButton("Next track", owner.NextTrack));
            var musicOptions = Row(); musicOptions.style.marginTop = 6;
            _shuffle = MakeButton("", () => { owner.Settings.Shuffle = !owner.Settings.Shuffle; owner.Remember(); UpdateControls(); }); _shuffle.style.flexGrow = 1;
            _repeat = MakeButton("", () => { owner.Settings.Repeat = !owner.Settings.Repeat; owner.Remember(); UpdateControls(); }); _repeat.style.flexGrow = 1; _repeat.style.marginLeft = 8;
            musicOptions.Add(_shuffle); musicOptions.Add(_repeat); _musicControls.Add(musicOptions); _player.Add(_musicControls);

            var volumeHeading = Row(); volumeHeading.style.marginTop = 16; volumeHeading.style.marginBottom = 6;
            var volumeLabel = Text("Volume", 20); volumeLabel.style.flexGrow = 1; volumeHeading.Add(volumeLabel);
            _volumeValue = Text(owner.Settings.Volume + "%", 20); volumeHeading.Add(_volumeValue); _player.Add(volumeHeading);
            var volume = new SliderInt(0, 100) { value = owner.Settings.Volume }; volume.style.minHeight = 28;
            volume.RegisterValueChangedCallback(e => { owner.Settings.Volume = e.newValue; owner.Remember(); SetText(_volumeValue, e.newValue + "%"); }); _player.Add(volume); SkinSlider(volume);
            _mute = MakeButton("", () => { owner.Settings.Muted = !owner.Settings.Muted; owner.Remember(); UpdateControls(); }); _mute.style.marginTop = 6; _player.Add(_mute);
            var soundLabel = Text("Radio sound", 20); soundLabel.style.marginTop = 16; soundLabel.style.marginBottom = 6; _player.Add(soundLabel);
            var sounds = Row();
            for (int i = 0; i < _sound.Length; i++)
            {
                var profile = (SoundProfile)i; _sound[i] = MakeButton(profile.ToString(), () => { owner.SetSound(profile); UpdateControls(); });
                _sound[i].style.flexGrow = 1; _sound[i].style.flexBasis = 0; _sound[i].style.minWidth = 0;
                if (i > 0) _sound[i].style.marginLeft = 6; sounds.Add(_sound[i]);
            }
            _player.Add(sounds);
            var distanceLabel = Text("Audible distance", 20); distanceLabel.style.marginTop = 16; distanceLabel.style.marginBottom = 6; _player.Add(distanceLabel);
            var distances = Row();
            for (int i = 0; i < _range.Length; i++)
            {
                bool nearby = i == 1;
                _range[i] = MakeButton(nearby ? "Nearby" : "Normal", () => { owner.SetShortRange(nearby); UpdateControls(); });
                _range[i].style.flexGrow = 1; _range[i].style.flexBasis = 0;
                _range[i].tooltip = nearby ? "Fades sharply with distance; quiet beyond the immediate area." : "The original wide RadioRamen range.";
                if (i > 0) _range[i].style.marginLeft = 8; distances.Add(_range[i]);
            }
            _player.Add(distances);
            _notice.style.color = Red; _notice.style.whiteSpace = WhiteSpace.Normal; _notice.style.marginTop = 8; _player.Add(_notice);
            _body.RegisterCallback<GeometryChangedEvent>(_ => { Resize(); });
            _root.RegisterCallback<NavigationCancelEvent>(e => { e.StopPropagation(); owner.Back(); });
            BorrowNotebookFont(notebook);
            Populate(owner.Browsing.View(_music).Scroll); UpdateStatus();
            header.schedule.Execute(() => close.Focus());
            _body.schedule.Execute(RecordLayout).StartingIn(180);
        }
        catch { UnityEngine.Object.Destroy(_object); UnityEngine.Object.Destroy(_settings); throw; }
    }

    private void BorrowNotebookFont(NotebookService notebook)
    {
        var document = AccessTools.Field(typeof(NotebookService), "m_NotebookUIDoc").GetValue(notebook) as UIDocument;
        var existingRoot = document ? document.rootVisualElement : null;
        var existing = existingRoot?.Q<Label>("instructions-desc-label") ?? existingRoot?.Q<Label>("recipe-description-label");
        if (existing != null && UseNotebookFont(existing)) return;
        // Keep native styles on the hidden sample only. Pause-menu selectors must
        // never style the station buttons, input fields or playback controls.
        var asset = AccessTools.Field(typeof(NotebookService), "instructionsUIAsset").GetValue(notebook) as VisualTreeAsset;
        if (!asset) throw new InvalidOperationException("The notebook body font is not available.");
        var sample = asset.CloneTree(); sample.style.position = Position.Absolute; sample.style.left = -10000; sample.style.top = -10000;
        sample.style.width = 1920; sample.style.height = 1080; sample.style.opacity = 0; sample.pickingMode = PickingMode.Ignore;
        foreach (var element in sample.Query<VisualElement>().ToList()) element.tabIndex = -1;
        _root.Add(sample);
        _root.schedule.Execute(() =>
        {
            if (_disposed) return;
            var text = sample.Q<Label>("instructions-desc-label") ?? sample.Q<Label>("instructions-preamble-label");
            if (text != null)
                UseNotebookFont(text);
            sample.RemoveFromHierarchy(); RecordLayout();
        }).StartingIn(50);
    }

    private bool UseNotebookFont(TextElement text)
    {
        _font = text.resolvedStyle.unityFontDefinition;
        if (_font.fontAsset == null && _font.font == null && text.resolvedStyle.unityFont)
            _font = FontDefinition.FromFont(text.resolvedStyle.unityFont);
        _hasFont = _font.fontAsset != null || _font.font != null;
        _fontName = _font.fontAsset ? _font.fontAsset.name : _font.font ? _font.font.name : "Notebook font unresolved";
        ApplyFont(_body); QueueBrowseRestore(); return _hasFont;
    }

    private void ApplyFont(VisualElement parent)
    {
        if (!_hasFont) return;
        parent.style.unityFontDefinition = _font;
        foreach (var text in parent.Query<TextElement>().ToList()) text.style.unityFontDefinition = _font;
    }

    private void Tab(bool music)
    {
        if (_music == music) return;
        RememberBrowse();
        _music = music; _search.SetValueWithoutNotify(_owner.Browsing.View(music).Search);
        ClosePicker(); Populate(_owner.Browsing.View(music).Scroll);
    }

    private Vector2 BrowseOffset => _restorePending ? _restoreOffset : _list.scrollOffset;

    private void RememberBrowse()
    {
        var state = _owner.Browsing;
        state.Music = _music; state.Category = _category; state.Region = _region; state.FavouritesOnly = _favouritesOnly;
        var view = state.View(_music); view.Search = _search.value; view.Scroll = BrowseOffset;
    }

    private void QueueBrowseRestore()
    {
        if (!_restorePending || _restoreQueued || _disposed) return;
        _restoreQueued = true;
        _list.schedule.Execute(() => {
            _restoreQueued = false;
            if (_disposed || !_restorePending || !_hasFont || _list.contentViewport.layout.height <= 0
                || float.IsNaN(_list.contentContainer.layout.height)) return;
            float maximum = Math.Max(0, _list.contentContainer.layout.height - _list.contentViewport.layout.height);
            _list.scrollOffset = new Vector2(0, Mathf.Clamp(_restoreOffset.y, 0, maximum));
            _restorePending = false;
        });
    }

    private void Populate(Vector2? restoreOffset = null)
    {
        if (_list == null) return;
        _list.Clear(); _items.Clear();
        if (!_owner.Stations.Any(s => s.Category == _category)) _category = "All categories";
        if (!_owner.Stations.Any(s => s.Region == _region)) _region = "All regions";
        _categoryButton.text = _category + "  v"; _regionButton.text = _region + "  v";
        _filters.style.display = _music ? DisplayStyle.None : DisplayStyle.Flex;
        _musicControls.style.display = _music ? DisplayStyle.Flex : DisplayStyle.None;
        _stationTools.style.display = _music ? DisplayStyle.None : DisplayStyle.Flex;
        int count = 0;
        if (_music)
        {
            foreach (string path in _owner.Tracks.Where(t => System.IO.Path.GetFileName(t).IndexOf(_search.value, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                string track = path; AddItem(System.IO.Path.GetFileNameWithoutExtension(track), "MP3 / Your music", track, () => _owner.ChooseTrack(track)); count++;
            }
            if (count == 0) { var empty = Text("Copy MP3 files into RadioRamen/Music, then choose Refresh.", 20); empty.style.color = Paper; empty.style.whiteSpace = WhiteSpace.Normal; _list.Add(empty); }
        }
        else foreach (var station in _owner.Stations.Where(s => s.Matches(_category, _region, _search.value) && (!_favouritesOnly || _owner.IsFavourite(s.Id))))
        {
            var selected = station; AddItem(station.Name, station.Detail, station.Id, () => _owner.Choose(selected), station); count++;
        }
        if (!_music && count == 0)
        {
            var empty = Text(_favouritesOnly ? "No matching favourites. Use a station's star to save it here." : "No matching stations. Try different filters or add a station.", 20);
            empty.style.color = Paper; empty.style.whiteSpace = WhiteSpace.Normal; _list.Add(empty);
        }
        SetText(_count, count + (_music ? " tracks" : " stations"));
        UpdateControls(); UpdateSelection(); ApplyFont(_list);
        _restoreOffset = restoreOffset ?? Vector2.zero; _restorePending = true; QueueBrowseRestore();
    }

    private void AddItem(string title, string detail, string source, Action action, Station? station = null)
    {
        var button = MakeButton("", action, Color.clear, Paper); button.style.minHeight = 74;
        button.style.flexDirection = FlexDirection.Column; button.style.alignItems = Align.Stretch; button.style.justifyContent = Justify.Center;
        button.style.paddingTop = button.style.paddingBottom = 8; button.style.marginBottom = 6;
        var name = Text(title, 22); name.style.color = Paper; name.style.whiteSpace = WhiteSpace.Normal; name.style.unityTextAlign = TextAnchor.MiddleLeft;
        name.enableRichText = false; name.style.flexShrink = 1; name.style.minWidth = 0;
        name.style.borderBottomWidth = 2; name.style.borderBottomColor = Color.clear; name.style.paddingBottom = 2;
        var heading = Row(); heading.style.alignItems = Align.FlexStart;
        var marker = Text("", 16); marker.style.width = 112; marker.style.flexShrink = 0; marker.style.marginLeft = 12;
        marker.style.paddingLeft = marker.style.paddingRight = 6; marker.style.paddingTop = marker.style.paddingBottom = 2;
        marker.style.backgroundColor = Paper; marker.style.color = Blue; marker.style.unityTextAlign = TextAnchor.MiddleCenter;
        marker.style.visibility = Visibility.Hidden; marker.pickingMode = PickingMode.Ignore; Round(marker, 4);
        heading.Add(name); heading.Add(Spacer()); heading.Add(marker);
        var caption = Text(detail, 17); caption.style.color = Paper; caption.style.whiteSpace = WhiteSpace.Normal; caption.style.marginTop = 2;
        caption.enableRichText = false;
        button.Add(heading); button.Add(caption);
        if (station == null) _list.Add(button);
        else
        {
            var row = Row(); row.style.alignItems = Align.FlexStart; row.style.marginBottom = 6;
            button.style.flexGrow = 1; button.style.flexBasis = 0; button.style.marginBottom = 0; row.Add(button);
            var star = FavouriteButton(station); star.style.marginLeft = 6; row.Add(star);
            if (station.IsCustom)
            {
                var edit = MakeButton("Edit", () => EditStation(station)); edit.style.fontSize = 17; edit.style.marginLeft = 6; edit.style.paddingLeft = edit.style.paddingRight = 6; edit.style.width = 48; edit.style.flexShrink = 0; row.Add(edit);
            }
            _list.Add(row);
        }
        _items.Add((button, name, marker, source));
        button.RegisterCallback<PointerEnterEvent>(_ => button.style.backgroundColor = CurrentSource == source ? Blue : WoodHover);
        button.RegisterCallback<PointerLeaveEvent>(_ => button.style.backgroundColor = CurrentSource == source ? Blue : Color.clear);
    }

    private string CurrentSource => _owner.Playing?.Local == true ? _owner.Playing.Source : _owner.CurrentStationId;
    private void UpdateSelection()
    {
        _selected = CurrentSource;
        _selectedMarker = _owner.PlaybackMarker;
        foreach (var item in _items)
        {
            bool current = item.source == _selected;
            item.button.style.backgroundColor = current ? Blue : Color.clear;
            item.name.style.borderBottomColor = current ? Paper : Color.clear;
            item.marker.style.visibility = current ? Visibility.Visible : Visibility.Hidden;
            item.marker.style.color = _selectedMarker == "Unavailable" ? Red : Blue;
            SetText(item.marker, current ? _selectedMarker : "");
        }
    }

    private void UpdateControls()
    {
        Select(_radioTab, !_music); Select(_musicTab, _music);
        _shuffle.text = "Shuffle: " + (_owner.Settings.Shuffle ? "On" : "Off"); Select(_shuffle, _owner.Settings.Shuffle);
        _repeat.text = "Repeat: " + (_owner.Settings.Repeat ? "On" : "Off"); Select(_repeat, _owner.Settings.Repeat);
        _mute.text = _owner.Settings.Muted ? "Muted" : "Mute"; Select(_mute, _owner.Settings.Muted);
        for (int i = 0; i < _sound.Length; i++) Select(_sound[i], (int)_owner.Settings.Sound == i);
        Select(_range[0], !_owner.Settings.ShortRange); Select(_range[1], _owner.Settings.ShortRange);
        Select(_favouritesButton, _favouritesOnly);
    }

    private Button FavouriteButton(Station station)
    {
        bool saved = _owner.IsFavourite(station.Id);
        var button = MakeButton("", () => {
            try
            {
                Vector2 offset = BrowseOffset;
                _owner.SetFavourite(station.Id, !_owner.IsFavourite(station.Id));
                Populate(offset); SetText(_notice, "");
            }
            catch (Exception ex) { SetText(_notice, ex.Message); }
        }, saved ? Blue : WoodHover, Paper);
        button.style.width = 40; button.style.height = 40; button.style.flexShrink = 0;
        button.style.paddingLeft = button.style.paddingRight = 7; button.style.alignItems = Align.Center; button.style.justifyContent = Justify.Center;
        button.tooltip = saved ? "Remove from favourites" : "Add to favourites";
        var icon = new VisualElement(); icon.style.width = icon.style.height = 22; icon.pickingMode = PickingMode.Ignore;
        icon.generateVisualContent += context => {
            var painter = context.painter2D; painter.BeginPath();
            for (int i = 0; i < 10; i++)
            {
                float angle = -Mathf.PI / 2 + i * Mathf.PI / 5, radius = i % 2 == 0 ? 10 : 4.5f;
                var point = new Vector2(11 + Mathf.Cos(angle) * radius, 11 + Mathf.Sin(angle) * radius);
                if (i == 0) painter.MoveTo(point); else painter.LineTo(point);
            }
            painter.ClosePath(); painter.strokeColor = Paper; painter.lineWidth = 1.5f;
            if (saved) { painter.fillColor = Paper; painter.Fill(); } painter.Stroke();
        };
        button.Add(icon); return button;
    }

    private void EditStation(Station? station)
    {
        ClosePicker(); CloseEditor();
        var overlay = new VisualElement(); Fill(overlay); overlay.style.backgroundColor = new Color(0.2f, 0.15f, 0.1f, 0.45f);
        overlay.style.justifyContent = Justify.Center; overlay.style.alignItems = Align.Center; _editor = overlay;
        var form = new ScrollView(ScrollViewMode.Vertical); form.style.width = 650; form.style.maxWidth = Length.Percent(92); form.style.maxHeight = Length.Percent(92);
        form.style.paddingLeft = form.style.paddingRight = 24; form.style.paddingTop = form.style.paddingBottom = 20;
        form.style.backgroundColor = Paper; form.style.color = Ink; Round(form, 12); SkinScroll(form, false);
        var title = Text(station == null ? "Add station" : "Edit station", 28); title.style.marginBottom = 10; form.Add(title);
        var help = Text("Use a direct HTTP(S) audio stream URL. Save keeps your current playback; Save & play starts this feed.", 18);
        help.style.whiteSpace = WhiteSpace.Normal; help.style.marginBottom = 12; form.Add(help);
        var name = EditorField(form, "Station name", station?.Name ?? "", 160);
        var stream = EditorField(form, "Stream URL", station?.Stream ?? "", 2048);
        var category = EditorField(form, "Category (optional)", station?.Category ?? "My stations", 80);
        var region = EditorField(form, "Region (optional)", station?.Region ?? "Online", 80);
        bool favourite = station != null && _owner.IsFavourite(station.Id);
        var favouriteButton = MakeButton("Favourite: " + (favourite ? "On" : "Off"), () => { }); Select(favouriteButton, favourite);
        favouriteButton.clicked += () => { favourite = !favourite; favouriteButton.text = "Favourite: " + (favourite ? "On" : "Off"); Select(favouriteButton, favourite); };
        favouriteButton.style.marginTop = 4; form.Add(favouriteButton);
        var error = Text("", 17); error.style.color = Red; error.style.whiteSpace = WhiteSpace.Normal; error.style.marginTop = error.style.marginBottom = 10; form.Add(error);
        void Save(bool play)
        {
            try
            {
                var saved = _owner.SaveCustomStation(station?.Id, name.value, stream.value, category.value, region.value, favourite);
                if (play) _owner.Choose(saved);
                _music = false; _favouritesOnly = false; _category = "All categories"; _region = "All regions"; _search.SetValueWithoutNotify("");
                CloseEditor(); Populate(); SetText(_notice, "");
                _restorePending = false;
                _list.schedule.Execute(() => { if (!_disposed) { var row = _items.FirstOrDefault(item => item.source == saved.Id); if (row.button != null) _list.ScrollTo(row.button); } });
            }
            catch (Exception ex) { SetText(error, ex.Message); }
        }
        var actions = Row();
        var cancel = MakeButton("Cancel", CloseEditor); cancel.style.flexGrow = 1; actions.Add(cancel);
        var save = MakeButton("Save", () => Save(false)); save.style.flexGrow = 1; save.style.marginLeft = 8; actions.Add(save);
        var play = MakeButton("Save & play", () => Save(true), Blue, Paper); play.style.flexGrow = 1; play.style.marginLeft = 8; actions.Add(play); form.Add(actions);
        if (station != null)
        {
            var removeRow = new VisualElement(); removeRow.style.marginTop = 16;
            void ShowRemove()
            {
                removeRow.Clear(); removeRow.Add(MakeButton("Remove this custom station", () => {
                    removeRow.Clear(); var question = Text("Remove this saved station?", 19); question.style.marginBottom = 8; removeRow.Add(question);
                    var choices = Row(); choices.Add(MakeButton("Keep", ShowRemove));
                    var remove = MakeButton("Remove", () => {
                        try { var offset = BrowseOffset; _owner.RemoveCustomStation(station.Id); CloseEditor(); Populate(offset); }
                        catch (Exception ex) { SetText(error, ex.Message); }
                    }, Red, Paper); remove.style.marginLeft = 8; choices.Add(remove); removeRow.Add(choices); ApplyFont(removeRow);
                }, Red, Paper)); ApplyFont(removeRow);
            }
            ShowRemove(); form.Add(removeRow);
        }
        overlay.Add(form); _root.Add(overlay); ApplyFont(form); form.schedule.Execute(() => name.Focus());
    }

    private static TextField EditorField(VisualElement form, string label, string value, int maximum)
    {
        var heading = Text(label, 19); heading.style.marginBottom = 4; form.Add(heading);
        var field = new TextField { value = value, maxLength = maximum, multiline = false }; SkinInput(field);
        field.style.marginBottom = 12; form.Add(field); return field;
    }

    private void CloseEditor() { _editor?.RemoveFromHierarchy(); _editor = null; }
    internal bool CloseTransient()
    {
        if (_editor != null) { CloseEditor(); return true; }
        if (_picker != null) { ClosePicker(); return true; }
        return false;
    }

    private void Picker(Button anchor, IEnumerable<string> values, string selected, Action<string> choose)
    {
        ClosePicker();
        string[] options = values.ToArray();
        var scrim = new VisualElement(); Fill(scrim); _picker = scrim;
        scrim.RegisterCallback<PointerDownEvent>(e => { if (ReferenceEquals(e.target, scrim)) { ClosePicker(); e.StopPropagation(); } });
        var box = new ScrollView(ScrollViewMode.Vertical); box.style.position = Position.Absolute;
        Vector2 at = _root.WorldToLocal(new Vector2(anchor.worldBound.xMin, anchor.worldBound.yMax + 4));
        float width = Math.Max(250, anchor.layout.width);
        box.style.left = Mathf.Clamp(at.x, 8, Math.Max(8, _root.layout.width - width - 8));
        box.style.top = Mathf.Clamp(at.y, 8, Math.Max(8, _root.layout.height - 360)); box.style.width = width; box.style.height = Math.Min(340, options.Length * 44 + 16);
        box.style.paddingLeft = box.style.paddingRight = 8; box.style.paddingTop = box.style.paddingBottom = 8;
        box.style.backgroundColor = Inset; Border(box, Wood, 2); Round(box, 8); SkinScroll(box, false);
        foreach (string value in options)
        {
            string choice = value; var button = MakeButton(choice, () => { ClosePicker(); choose(choice); anchor.Focus(); });
            button.style.marginBottom = 4; Select(button, choice == selected); box.Add(button);
        }
        scrim.Add(box); _root.Add(scrim); ApplyFont(box);
        box.Query<Button>().First()?.Focus();
    }

    private void ClosePicker() { _picker?.RemoveFromHierarchy(); _picker = null; }

    private void Resize()
    {
        bool narrow = _body.contentRect.width < 760;
        _main.style.flexDirection = narrow ? FlexDirection.Column : FlexDirection.Row;
        _library.style.marginRight = narrow ? 0 : 18; _library.style.marginBottom = narrow ? 12 : 0;
        _player.style.width = narrow ? new StyleLength(StyleKeyword.Auto) : new StyleLength(310);
        _player.style.maxHeight = narrow ? new StyleLength(260) : new StyleLength(StyleKeyword.None);
    }

    private void RecordLayout()
    {
        if (_disposed) return;
        _owner.NotePanel("Notebook paper; font " + _fontName + "; body 20, stations 22, details 17; canvas "
            + Math.Round(_root.layout.width) + "x" + Math.Round(_root.layout.height) + "; list " + Math.Round(_list.layout.width) + "x" + Math.Round(_list.layout.height)
            + "; tab " + (_music ? "music" : "radio") + "; list offset " + Math.Round(BrowseOffset.y));
    }

    internal void UpdateStatus()
    {
        var playing = _owner.Playing;
        SetText(_title, playing?.Title ?? (_music ? "Choose an MP3" : "Choose a station"));
        SetText(_status, _owner.Status);
        _status.style.color = playing?.Failure.Length > 0 ? Red : MutedInk;
        SetText(_position, playing?.Local == true ? TimeSpan.FromSeconds(_owner.MusicPosition).ToString(_owner.MusicPosition >= 3600 ? @"h\:mm\:ss" : @"m\:ss") : "");
        _position.style.display = playing?.Local == true ? DisplayStyle.Flex : DisplayStyle.None;
        if (_selected != CurrentSource || _selectedMarker != _owner.PlaybackMarker) UpdateSelection();
    }

    private static void SetText(Label label, string value) { if (label.text != value) label.text = value; }
    private static Label Text(string text, int size)
    {
        var label = new Label(text); label.style.fontSize = size; label.style.color = Ink;
        label.style.backgroundImage = StyleKeyword.None; label.style.backgroundColor = Color.clear;
        label.style.marginLeft = label.style.marginRight = label.style.marginTop = label.style.marginBottom = 0;
        label.style.paddingLeft = label.style.paddingRight = label.style.paddingTop = label.style.paddingBottom = 0;
        label.style.unityTextAlign = TextAnchor.MiddleLeft; return label;
    }

    private Button MakeButton(string text, Action action, Color? background = null, Color? foreground = null)
    {
        Color normal = background ?? Paper, ink = foreground ?? Ink;
        var button = new Button(action) { text = text };
        button.style.backgroundImage = StyleKeyword.None; button.style.backgroundColor = normal; button.style.color = ink;
        button.style.fontSize = 20; button.style.unityFontStyleAndWeight = FontStyle.Normal; button.style.unityTextAlign = TextAnchor.MiddleCenter;
        button.style.minHeight = 40; button.style.minWidth = 0; button.style.flexShrink = 1; button.style.whiteSpace = WhiteSpace.Normal;
        button.style.marginLeft = button.style.marginRight = button.style.marginTop = button.style.marginBottom = 0;
        button.style.paddingLeft = button.style.paddingRight = 12; button.style.paddingTop = button.style.paddingBottom = 5;
        Border(button, new Color(Wood.r, Wood.g, Wood.b, 0.35f), 1); Round(button, 6);
        button.RegisterCallback<PointerEnterEvent>(_ => button.style.opacity = 0.8f);
        button.RegisterCallback<PointerLeaveEvent>(_ => button.style.opacity = 1);
        return button;
    }

    private static void Select(Button button, bool selected) { button.style.backgroundColor = selected ? Blue : Paper; button.style.color = selected ? Paper : Ink; }
    private static void SkinInput(TextField field)
    {
        field.style.fontSize = 20; field.style.color = Ink; field.style.minHeight = 40;
        var input = field.Q<VisualElement>(className: "unity-text-field__input") ?? field.Q<VisualElement>(className: "unity-base-text-field__input");
        if (input == null) return;
        input.style.backgroundImage = StyleKeyword.None; input.style.backgroundColor = Inset; input.style.color = Ink;
        input.style.paddingLeft = input.style.paddingRight = 10; Border(input, Wood, 1); Round(input, 6);
        foreach (var text in input.Query<TextElement>().ToList()) { text.style.color = Ink; text.style.fontSize = 20; }
    }

    private static void SkinSlider(SliderInt slider)
    {
        var track = slider.Q("unity-tracker"); if (track != null) { track.style.backgroundImage = StyleKeyword.None; track.style.backgroundColor = Wood; track.style.height = 5; Round(track, 3); Border(track, Color.clear, 0); }
        var thumb = slider.Q("unity-dragger"); if (thumb != null) { thumb.style.backgroundImage = StyleKeyword.None; thumb.style.backgroundColor = Blue; thumb.style.width = 16; thumb.style.height = 24; Round(thumb, 4); Border(thumb, Color.clear, 0); }
        var edge = slider.Q("unity-dragger-border"); if (edge != null) { edge.style.backgroundImage = StyleKeyword.None; Border(edge, Color.clear, 0); }
    }

    private static void SkinScroll(ScrollView view, bool brown)
    {
        var bar = view.verticalScroller; bar.style.width = 14;
        bar.lowButton.style.display = bar.highButton.style.display = DisplayStyle.None;
        var track = bar.slider.Q("unity-tracker"); if (track != null) { track.style.backgroundColor = brown ? WoodHover : Paper; track.style.backgroundImage = StyleKeyword.None; Round(track, 6); Border(track, Color.clear, 0); }
        var thumb = bar.slider.Q("unity-dragger"); if (thumb != null) { thumb.style.backgroundColor = brown ? Paper : Wood; thumb.style.backgroundImage = StyleKeyword.None; thumb.style.minHeight = 28; Round(thumb, 6); Border(thumb, Color.clear, 0); }
    }

    private static VisualElement Row() { var row = new VisualElement(); row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center; return row; }
    private static VisualElement Spacer() { var element = new VisualElement(); element.style.flexGrow = 1; return element; }
    private static void Fill(VisualElement element) { element.style.position = Position.Absolute; element.style.left = element.style.right = element.style.top = element.style.bottom = 0; }
    private static void Round(VisualElement element, float value) { element.style.borderTopLeftRadius = element.style.borderTopRightRadius = element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = value; }
    private static void Border(VisualElement element, Color color, float width) { element.style.borderTopColor = element.style.borderRightColor = element.style.borderBottomColor = element.style.borderLeftColor = color; element.style.borderTopWidth = element.style.borderRightWidth = element.style.borderBottomWidth = element.style.borderLeftWidth = width; }
    private static Color Hex(int rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

    public void Dispose()
    {
        if (_disposed) return;
        RememberBrowse(); RecordLayout(); _disposed = true;
        UnityEngine.Object.Destroy(_object); UnityEngine.Object.Destroy(_settings);
    }
}
