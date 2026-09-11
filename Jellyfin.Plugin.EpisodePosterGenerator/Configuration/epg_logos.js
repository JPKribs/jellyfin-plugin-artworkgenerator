import { initCollapsibles, setTabs, createShared } from '/web/configurationpage?name=epg_jpkribs_shared.js';

export default function (view) {
    'use strict';

    var pluginId = 'b8715e44-6b77-4c88-9c74-2b6f4c7b9a1e';
    var shared = createShared(view, pluginId, 'Plugins/EpisodePosterGenerator');
    var fullConfig = null;
    var logoDesigns = [];
    var currentLogoId = null;
    var _initialized = false;
    var _dirty = false;
    var _saving = false;
    var _snapshot = null;
    var _previewObjectUrl = null;
    var _previewSeq = 0;
    var _fontsPromise = null;

    var LOGO_DEFAULTS = {
        TitleSource: 'Title',
        StripYear: true,
        SubtitleMode: 'Keep',
        SecondarySize: 45,
        Fill: 'Color',
        CustomRegex: '',
        Uppercase: false,
        FontFamily: 'Arial',
        FontStyle: 'Bold',
        UseCustomFont: false,
        FontPath: '',
        ColorSource: 'Fixed',
        Color: '#FFFFFFFF',
        OutlineEnabled: false,
        OutlineColor: '#FF000000',
        OutlineWidth: 4,
        ShadowEnabled: false,
        MaxLines: 2,
        Width: 800,
        Height: 310
    };

    function getTabs() {
        return [
            { href: 'configurationpage?name=epg_posters', name: 'Designs' },
            { href: 'configurationpage?name=epg_logos', name: 'Logos' },
            { href: 'configurationpage?name=epg_profiles', name: 'Profiles' },
            { href: 'configurationpage?name=epg_settings', name: 'Settings' }
        ];
    }

    // ── Utilities ────────────────────────────────────────────

    function generateGuid() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
    }

    function debounce(fn, delay) {
        var timer = null;
        return function () {
            var ctx = this, args = arguments;
            clearTimeout(timer);
            timer = setTimeout(function () { fn.apply(ctx, args); }, delay);
        };
    }

    function trapFocus(container, e) {
        if (e.key !== 'Tab') return;
        var focusable = container.querySelectorAll('button:not([disabled]), input:not([disabled]), select:not([disabled])');
        if (focusable.length === 0) return;
        var first = focusable[0];
        var last = focusable[focusable.length - 1];
        if (e.shiftKey && document.activeElement === first) { e.preventDefault(); last.focus(); }
        else if (!e.shiftKey && document.activeElement === last) { e.preventDefault(); first.focus(); }
    }

    function parseARGBHex(input) {
        if (!input) return null;
        input = input.replace('#', '').toUpperCase();
        if (input.length === 8) return { rgb: '#' + input.substring(2), alpha: parseInt(input.substring(0, 2), 16) };
        if (input.length === 6) return { rgb: '#' + input, alpha: 255 };
        return null;
    }

    function sameId(a, b) {
        return String(a || '').replace(/-/g, '').toLowerCase() === String(b || '').replace(/-/g, '').toLowerCase();
    }

    // ── Unsaved Changes ─────────────────────────────────────

    function snapshot() {
        return JSON.stringify(logoDesigns);
    }

    function setDirty(dirty) {
        _dirty = dirty;
        var indicator = view.querySelector('#unsavedIndicator');
        if (indicator) indicator.classList.toggle('visible', dirty);
    }

    function checkDirty() {
        if (!fullConfig || _snapshot === null) return;
        saveCurrentSettings();
        setDirty(snapshot() !== _snapshot);
    }

    function flashSaveSuccess() {
        var indicator = view.querySelector('#unsavedIndicator');
        if (!indicator) return;

        indicator.innerHTML = '';
        var dot = document.createElement('span');
        dot.className = 'jpk-unsaved-dot';
        dot.style.background = 'var(--epg-success-text)';
        indicator.appendChild(dot);
        indicator.appendChild(document.createTextNode(' Saved!'));
        indicator.classList.add('visible', 'save-success');

        setTimeout(function () {
            indicator.classList.remove('visible', 'save-success');
            setTimeout(function () {
                indicator.innerHTML = '<span class="jpk-unsaved-dot"></span> Unsaved changes';
            }, 300);
        }, 2000);
    }

    // ── Input Modal ─────────────────────────────────────────

    function showInputModal(title, fields, callback) {
        var triggerElement = document.activeElement;
        var modal = view.querySelector('#inputModal');
        var modalContent = view.querySelector('#inputModal .jpk-dialog-content');
        var fieldsContainer = view.querySelector('#inputModalFields');
        var btnConfirm = view.querySelector('#btnConfirmInputModal');
        var btnCancel = view.querySelector('#btnCancelInputModal');
        var btnClose = view.querySelector('#btnCloseInputModal');

        view.querySelector('#inputModalTitle').textContent = title;
        fieldsContainer.innerHTML = '';

        fields.forEach(function (field, index) {
            var container = document.createElement('div');
            container.className = 'jpk-modal-field';
            var label = document.createElement('label');
            label.className = 'jpk-modal-field-label';
            label.setAttribute('for', 'inputModalField_' + index);
            label.textContent = field.label;
            container.appendChild(label);
            var input = document.createElement('input');
            input.setAttribute('is', 'emby-input');
            input.type = 'text';
            input.id = 'inputModalField_' + index;
            input.value = field.value || '';
            if (field.placeholder) input.placeholder = field.placeholder;
            container.appendChild(input);
            fieldsContainer.appendChild(container);
        });

        modal.style.display = 'flex';
        var firstInput = fieldsContainer.querySelector('input');
        if (firstInput) setTimeout(function () { firstInput.focus(); firstInput.select(); }, 100);

        function cleanup() {
            btnConfirm.removeEventListener('click', onConfirm);
            btnCancel.removeEventListener('click', onCancel);
            btnClose.removeEventListener('click', onCancel);
            modal.removeEventListener('click', onBackdrop);
            document.removeEventListener('keydown', onKeydown);
            modal.style.display = 'none';
            if (triggerElement && triggerElement.focus) triggerElement.focus();
        }

        function onConfirm() {
            var values = [];
            fieldsContainer.querySelectorAll('input').forEach(function (input) { values.push(input.value); });
            cleanup();
            callback(values);
        }

        function onCancel() { cleanup(); callback(null); }
        function onBackdrop(e) { if (e.target === modal) onCancel(); }
        function onKeydown(e) {
            if (e.key === 'Enter') { e.preventDefault(); onConfirm(); }
            else if (e.key === 'Escape') { e.preventDefault(); onCancel(); }
            else { trapFocus(modalContent, e); }
        }

        btnConfirm.addEventListener('click', onConfirm);
        btnCancel.addEventListener('click', onCancel);
        btnClose.addEventListener('click', onCancel);
        modal.addEventListener('click', onBackdrop);
        document.addEventListener('keydown', onKeydown);
    }

    // ── Color Controls ──────────────────────────────────────

    function bindColorControls() {
        view.querySelectorAll('.color-picker').forEach(function (picker) {
            var container = picker.parentElement;
            var slider = container.querySelector('.alpha-slider');
            var label = container.querySelector('.alpha-label');
            var hex = container.querySelector('.hex-input');

            function fromControls() {
                var alpha = parseInt(slider.value, 10);
                hex.value = '#' + alpha.toString(16).padStart(2, '0').toUpperCase() + picker.value.substring(1).toUpperCase();
            }

            picker.addEventListener('input', function () { fromControls(); onSettingChanged(); });
            slider.addEventListener('input', function () { label.textContent = slider.value; fromControls(); onSettingChanged(); });
            hex.addEventListener('input', function () { syncColorControls(); onSettingChanged(); });
        });
    }

    function syncColorControls() {
        view.querySelectorAll('.color-picker').forEach(function (picker) {
            var container = picker.parentElement;
            var parsed = parseARGBHex(container.querySelector('.hex-input').value);
            if (!parsed) return;
            picker.value = parsed.rgb;
            container.querySelector('.alpha-slider').value = parsed.alpha;
            container.querySelector('.alpha-label').textContent = parsed.alpha;
        });
    }

    // ── Loading ─────────────────────────────────────────────

    function loadFonts() {
        if (_fontsPromise) return _fontsPromise;

        _fontsPromise = ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl('Plugins/EpisodePosterGenerator/Fonts'),
            dataType: 'json'
        }).then(function (families) {
            if (!families || !families.length) return;
            var select = view.querySelector('#selectLogoFontFamily');
            select.innerHTML = '';
            families.forEach(function (family) {
                var option = document.createElement('option');
                option.value = family;
                option.textContent = family;
                select.appendChild(option);
            });
            if (families.indexOf(select.getAttribute('data-default')) === -1) {
                select.setAttribute('data-default', families[0]);
            }
        }).catch(function (error) {
            console.error('Failed to load server font families:', error);
        });

        return _fontsPromise;
    }

    // Logo designs live in their own file on the server rather than in the plugin configuration,
    // so they come from their own endpoint. The configuration is still read, to name the profiles
    // that use a design being deleted.
    function fetchLogos() {
        return ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl('Plugins/EpisodePosterGenerator/Logos'),
            dataType: 'json'
        });
    }

    function loadConfig() {
        Dashboard.showLoadingMsg();
        Promise.all([loadFonts(), fetchLogos(), shared.getConfig()]).then(function (results) {
            logoDesigns = results[1] || [];
            fullConfig = results[2] || {};
            fullConfig.Profiles = fullConfig.Profiles || [];

            // The server always supplies one; this only guards a malformed payload.
            if (logoDesigns.length === 0) {
                logoDesigns.push({ Id: generateGuid(), Name: 'Default', Settings: Object.assign({}, LOGO_DEFAULTS) });
            }

            populateDropdown();
            _snapshot = snapshot();
            setDirty(false);
            Dashboard.hideLoadingMsg();
        }).catch(function (error) {
            console.error('Failed to load logo designs:', error);
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Failed to load logo designs. Please reload the page.', 'Error');
        });
    }

    function populateDropdown() {
        var select = view.querySelector('#selectLogo');
        var logos = logoDesigns.slice().sort(function (a, b) {
            return (a.Name || '').localeCompare(b.Name || '');
        });

        select.innerHTML = '';
        logos.forEach(function (logo) {
            var option = document.createElement('option');
            option.value = logo.Id;
            option.textContent = logo.Name || 'Unnamed Logo';
            select.appendChild(option);
        });

        if (!currentLogoId || !logos.some(function (l) { return l.Id === currentLogoId; })) {
            currentLogoId = logos[0].Id;
        }

        select.value = currentLogoId;
        loadCurrentLogo();
    }

    function getCurrentLogo() {
        return logoDesigns.find(function (l) { return l.Id === currentLogoId; });
    }

    function applySelectValue(el, value) {
        if (value !== undefined && value !== null && value !== '') {
            el.value = String(value);
            if (el.selectedIndex !== -1) return;

            // Keep a stored value selectable rather than silently rewriting it.
            var preserved = document.createElement('option');
            preserved.value = value;
            preserved.textContent = value + ' (unavailable)';
            el.insertBefore(preserved, el.firstChild);
            el.value = String(value);
            return;
        }

        el.value = el.getAttribute('data-default') || '';
        if (el.selectedIndex === -1) el.selectedIndex = 0;
    }

    function loadCurrentLogo() {
        var logo = getCurrentLogo();
        if (!logo) return;
        logo.Settings = logo.Settings || {};

        view.querySelectorAll('[data-setting]').forEach(function (el) {
            var key = el.getAttribute('data-setting');
            var value = logo.Settings[key];
            if (value === undefined || value === null) value = LOGO_DEFAULTS[key];

            if (el.type === 'checkbox') {
                el.checked = value === true;
            } else if (el.tagName === 'SELECT') {
                applySelectValue(el, value);
            } else {
                el.value = value;
            }
        });

        syncColorControls();
        updateVisibility();
        schedulePreview();
    }

    function saveCurrentSettings() {
        var logo = getCurrentLogo();
        if (!logo) return;
        logo.Settings = logo.Settings || {};

        view.querySelectorAll('[data-setting]').forEach(function (el) {
            var key = el.getAttribute('data-setting');
            if (el.type === 'checkbox') {
                logo.Settings[key] = el.checked;
            } else if (el.getAttribute('data-type') === 'number') {
                var number = parseFloat(el.value);
                logo.Settings[key] = isNaN(number) ? LOGO_DEFAULTS[key] : number;
            } else {
                logo.Settings[key] = el.value;
            }
        });
    }

    // ── Visibility ──────────────────────────────────────────

    function updateVisibility() {
        var mode = view.querySelector('#selectSubtitleMode').value;
        var twoSizes = mode === 'TitleLarge' || mode === 'SubtitleLarge';
        view.querySelector('#secondarySizeContainer').style.display = twoSizes ? 'block' : 'none';

        // Sampling takes the colour from the artwork, so leaving a swatch on screen would imply the
        // chosen colour still paints the text. The control collapses to the opacity it does control.
        var sampling = view.querySelector('#selectColorSource').value !== 'Fixed';
        var group = view.querySelector('#txtLogoColor').closest('.color-control-group');
        if (group) group.classList.toggle('palette-derived', sampling);

        var colorLabel = view.querySelector('label[for="txtLogoColor"]');
        if (colorLabel) {
            colorLabel.textContent = colorLabel.getAttribute(sampling ? 'data-opacity-label' : 'data-color-label');
        }

        var colorDesc = view.querySelector('#descLogoColor');
        if (colorDesc) {
            colorDesc.textContent = colorDesc.getAttribute(sampling ? 'data-opacity-desc' : 'data-color-desc');
        }

        // A frame fill has no colour to pick, so the colour controls step aside for it.
        var fill = view.querySelector('#selectLogoFill').value;
        view.querySelectorAll('[data-hide-for-fill]').forEach(function (el) {
            el.style.display = el.getAttribute('data-hide-for-fill') === fill ? 'none' : 'block';
        });

        view.querySelectorAll('[data-depends-on]').forEach(function (el) {
            var cb = view.querySelector('#' + el.getAttribute('data-depends-on'));
            el.style.display = cb && cb.checked ? 'block' : 'none';
        });

        view.querySelectorAll('[data-hide-when-checked]').forEach(function (el) {
            var cb = view.querySelector('#' + el.getAttribute('data-hide-when-checked'));
            el.style.display = cb && cb.checked ? 'none' : 'block';
        });
    }

    // ── Live Preview ────────────────────────────────────────

    function renderPreview() {
        if (!fullConfig) return;
        saveCurrentSettings();
        var logo = getCurrentLogo();
        if (!logo) return;

        var img = view.querySelector('#logoPreviewImage');
        var status = view.querySelector('#logoPreviewStatus');
        status.textContent = 'Rendering…';
        status.style.display = 'block';

        // Only the latest request may update the image, so a slow older response cannot win.
        var seq = ++_previewSeq;

        // The sample name is a preview aid only, so it travels in the query rather than the design.
        var sample = view.querySelector('#txtSampleName').value.trim();
        var url = ApiClient.getUrl('Plugins/EpisodePosterGenerator/Preview/Logo', sample ? { name: sample } : undefined);

        ApiClient.ajax({
            type: 'POST',
            url: url,
            data: JSON.stringify(logo.Settings),
            contentType: 'application/json'
        }).then(function (response) {
            if (!response.ok) throw new Error('Preview failed: ' + response.status);
            return response.blob();
        }).then(function (blob) {
            if (seq !== _previewSeq) return;
            if (_previewObjectUrl) URL.revokeObjectURL(_previewObjectUrl);
            _previewObjectUrl = URL.createObjectURL(blob);
            img.src = _previewObjectUrl;
            img.style.display = 'block';
            status.style.display = 'none';
        }).catch(function (error) {
            if (seq !== _previewSeq) return;
            console.error('Logo preview error:', error);
            status.textContent = 'Preview unavailable';
            status.style.display = 'block';
        });
    }

    var schedulePreview = debounce(renderPreview, 400);

    function onSettingChanged() {
        updateVisibility();
        checkDirty();
        schedulePreview();
    }

    // ── Logo CRUD ───────────────────────────────────────────

    function nameIsTaken(name, exceptId) {
        return logoDesigns.some(function (l) {
            return l.Id !== exceptId && l.Name && l.Name.toLowerCase() === name.toLowerCase();
        });
    }

    function profilesUsingLogo(logoId) {
        return fullConfig.Profiles.filter(function (p) {
            return (p.Slots || []).some(function (s) { return s.Slot === 'Logo' && sameId(s.DesignId, logoId); });
        });
    }

    function createNewLogo() {
        showInputModal('New Logo Design', [{ label: 'Name', placeholder: 'Uppercase Outline' }], function (values) {
            if (!values) return;
            var name = (values[0] || '').trim();
            if (!name) { Dashboard.alert('Name is required.'); return; }
            if (nameIsTaken(name)) { Dashboard.alert('A logo design with this name already exists.'); return; }

            saveCurrentSettings();
            var source = getCurrentLogo();
            var created = {
                Id: generateGuid(),
                Name: name,
                Settings: Object.assign({}, LOGO_DEFAULTS, source ? source.Settings : {})
            };

            logoDesigns.push(created);
            currentLogoId = created.Id;
            populateDropdown();
            checkDirty();
        });
    }

    function renameCurrentLogo() {
        var logo = getCurrentLogo();
        showInputModal('Rename Logo Design', [{ label: 'Name', value: logo.Name || '' }], function (values) {
            if (!values) return;
            var name = (values[0] || '').trim();
            if (!name) return;
            if (nameIsTaken(name, logo.Id)) { Dashboard.alert('A logo design with this name already exists.'); return; }

            logo.Name = name;
            populateDropdown();
            checkDirty();
        });
    }

    function deleteCurrentLogo() {
        if (logoDesigns.length <= 1) {
            Dashboard.alert('Keep at least one logo design. Turn logos off in a profile instead.');
            return;
        }

        var logo = getCurrentLogo();
        var users = profilesUsingLogo(logo.Id).map(function (p) { return p.Name || 'Unnamed'; });
        var message = users.length
            ? 'This logo design is used by: ' + users.join(', ') + '. Those logos will use the default design instead. Delete it anyway?'
            : 'Delete this logo design?';

        Dashboard.confirm(message, 'Delete Logo Design', function (confirmed) {
            if (!confirmed) return;
            logoDesigns = logoDesigns.filter(function (l) { return l.Id !== logo.Id; });
            currentLogoId = null;
            populateDropdown();
            checkDirty();
        });
    }

    // ── Save ────────────────────────────────────────────────

    function saveConfig() {
        saveCurrentSettings();
        if (_saving) return;
        _saving = true;
        Dashboard.showLoadingMsg();

        ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl('Plugins/EpisodePosterGenerator/Logos'),
            data: JSON.stringify(logoDesigns),
            contentType: 'application/json'
        }).then(function (response) {
            if (!response.ok) throw new Error('Save failed: ' + response.status);
            _snapshot = snapshot();
            setDirty(false);
            flashSaveSuccess();
            Dashboard.hideLoadingMsg();
        }).catch(function (error) {
            console.error('Failed to save logo designs:', error);
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Failed to save. Please check the server log and try again.', 'Error');
        }).finally(function () {
            _saving = false;
        });
    }

    // ── Event Binding ───────────────────────────────────────

    function bindEventListeners() {
        view.querySelector('#EpgLogosForm').addEventListener('submit', function (e) {
            e.preventDefault();
            saveConfig();
        });

        view.querySelector('#selectLogo').addEventListener('change', function () {
            saveCurrentSettings();
            currentLogoId = this.value;
            loadCurrentLogo();
        });

        view.querySelector('#btnNewLogo').addEventListener('click', createNewLogo);
        view.querySelector('#btnRenameLogo').addEventListener('click', renameCurrentLogo);
        view.querySelector('#btnDeleteLogo').addEventListener('click', deleteCurrentLogo);

        view.querySelector('#txtSampleName').addEventListener('input', schedulePreview);

        view.querySelector('#btnToggleLogoBackground').addEventListener('click', function () {
            var frame = view.querySelector('#logoPreviewFrame');
            var light = frame.classList.toggle('light');
            this.querySelector('span').textContent = light ? 'Dark Background' : 'Light Background';
        });

        view.querySelectorAll('[data-setting]').forEach(function (el) {
            if (el.classList.contains('hex-input')) return;
            var evt = (el.type === 'checkbox' || el.tagName === 'SELECT') ? 'change' : 'input';
            el.addEventListener(evt, onSettingChanged);
        });
    }

    // ── Lifecycle ───────────────────────────────────────────

    function onBeforeUnload(e) {
        if (_dirty) {
            e.preventDefault();
            e.returnValue = '';
        }
    }

    view.addEventListener('viewshow', function () {
        setTabs('epg', 1, getTabs());

        if (!_initialized) {
            _initialized = true;
            initCollapsibles(view);
            bindEventListeners();
            bindColorControls();
        }

        window.addEventListener('beforeunload', onBeforeUnload);
        loadConfig();
    });

    view.addEventListener('viewdestroy', function () {
        if (_previewObjectUrl) {
            URL.revokeObjectURL(_previewObjectUrl);
            _previewObjectUrl = null;
        }
    });

    view.addEventListener('viewbeforehide', function (e) {
        window.removeEventListener('beforeunload', onBeforeUnload);

        if (_dirty) {
            var confirmed = confirm('You have unsaved changes. Are you sure you want to leave?');
            if (!confirmed) {
                e.preventDefault();
                setTabs('epg', 1, getTabs());
            }
        }
    });
}
