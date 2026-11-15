// ag_logos.js
// The Logos tab, which edits text logo designs and previews them against a sample name.

import {
    bindColorField,
    createArtworkPage,
    createPreviewFrame,
    confirmDialog,
    generateGuid,
    initCollapsibles,
    promptName,
    sameId
} from './configurationpage?name=ag_shared.js';

export default function (view) {
    'use strict';

    var fullConfig = null;
    var logoDesigns = [];
    var currentLogoId = null;
    var _saving = false;
    var _fontsPromise = null;
    var _preview = null;
    var logoSettingText = {};
    var logoSettingOptions = {};
    var logoSettingDefaults = {};

    var page = createArtworkPage(view, 1, {
        bind: function () {
            initCollapsibles(view);
            buildPreview();
            bindEventListeners();
            bindColorControls();
        },
        load: loadConfig
    });
    var shared = page.shared;

    var colorFields = [];

    // snapshot
    // Serializes the logo designs, so the change tracker can tell an edit from the last load or save.
    //
    // Output
    // [string]: The designs as JSON.
    function snapshot() {
        return JSON.stringify(logoDesigns);
    }

    var tracker = shared.createChangeTracker({ serialize: snapshot, indicatorId: 'unsavedIndicator', savedText: 'Saved' });
    page.guardUnsaved(tracker);

    // checkDirty
    // Reads the fields into the design on screen and shows the unsaved indicator while the designs differ from the last load or save.
    function checkDirty() {
        if (!fullConfig) return;
        saveCurrentSettings();
        tracker.check();
    }

    // bindColorControls
    // Draws each color setting with Base's Color Picker, and its opacity alone with Base's slider when the image gives the color.
    function bindColorControls() {
        view.querySelectorAll('.ag-color-field').forEach(function (field) {
            var handle = bindColorField(field, onSettingChanged);
            handle.field = field;
            colorFields.push(handle);
        });
    }

    // syncColorControls
    // Redraws every color setting from its stored value.
    function syncColorControls() {
        colorFields.forEach(function (handle) { handle.sync(); });
    }

    // A logo setting's choices, default, label, and help text all come from LogoSettings in C#, the
    // same way the Designs page reads them off PosterSettings.
    function loadSettingMetadata() {
        return ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl('Plugins/ArtworkGenerator/SettingOptions'),
            dataType: 'json'
        }).then(function (payload) {
            logoSettingText = (payload && (payload.text || payload.Text)) || {};
            logoSettingOptions = (payload && (payload.options || payload.Options)) || {};
            logoSettingDefaults = (payload && (payload.logoDefaults || payload.LogoDefaults)) || {};
            populateSettingOptions();
            applySettingText();
        }).catch(function (error) {
            console.error('Failed to load setting text:', error);
        });
    }

    // Choices come from the settings model too, so a value added to a logo enum in C# appears here.
    // Font families are left alone: they are filled from the fonts installed on the server.
    function populateSettingOptions() {
        view.querySelectorAll('select[data-setting]').forEach(function (select) {
            var key = select.getAttribute('data-setting');
            if (key === 'FontFamily') return;

            var options = logoSettingOptions[key];
            if (!options || !options.length) return;

            var previous = select.value;
            select.innerHTML = '';

            options.forEach(function (option) {
                var el = document.createElement('option');
                el.value = option.value !== undefined ? option.value : option.Value;
                el.textContent = option.label !== undefined ? option.label : option.Label;
                select.appendChild(el);
            });

            if (previous) select.value = previous;
        });
    }

    function applySettingText() {
        view.querySelectorAll('[data-setting]').forEach(function (el) {
            var key = el.getAttribute('data-setting');
            var text = logoSettingText[key];
            if (!text) return;

            var container = el.closest('.inputContainer, .checkboxContainer, .base-field');
            if (!container) return;

            var label = container.querySelector('span.checkboxLabel') || container.querySelector('label');

            // The color field swaps its own wording between color and opacity, so it keeps it.
            if (label && !label.hasAttribute('data-color-label')) {
                var name = text.label || text.Label || '';
                label.textContent = name;
            }

            var description = container.querySelector('.fieldDescription');
            var help = text.description || text.Description || '';
            if (description && help && !description.hasAttribute('data-color-desc')) {
                description.textContent = help;
            }
        });
    }

    function loadFonts() {
        if (_fontsPromise) return _fontsPromise;

        _fontsPromise = ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl('Plugins/ArtworkGenerator/Fonts'),
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
            url: ApiClient.getUrl('Plugins/ArtworkGenerator/Logos'),
            dataType: 'json'
        });
    }

    function loadConfig() {
        Promise.all([loadFonts(), fetchLogos(), shared.getConfig(), loadSettingMetadata()]).then(function (results) {
            logoDesigns = results[1] || [];
            fullConfig = results[2] || {};
            fullConfig.Profiles = fullConfig.Profiles || [];

            // The server always supplies one; this only guards a malformed payload.
            if (logoDesigns.length === 0) {
                logoDesigns.push({ Id: generateGuid(), Name: 'Default', Settings: Object.assign({}, logoSettingDefaults) });
            }

            populateDropdown();
            saveCurrentSettings();
            tracker.markClean();
        }).catch(function (error) {
            console.error('Failed to load logo designs:', error);
            shared.setStatus('saveStatus', 'Could not load the logo designs. Reload the page.', true, { timeout: 0 });
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
            if (value === undefined || value === null) value = logoSettingDefaults[key];

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
                logo.Settings[key] = isNaN(number) ? logoSettingDefaults[key] : number;
            } else {
                logo.Settings[key] = el.value;
            }
        });
    }

    // Where a sampled color comes from. The preview draws its logo over the bundled demo art, and
    // these are the same two images the renderer samples: the item's own poster, or its backdrop.
    var COLOR_SOURCES = {
        SeriesPoster: { component: 'poster', label: 'Color sampled from this poster' },
        SeriesBackdrop: { component: 'canvas', label: 'Color sampled from this backdrop' }
    };

    var _colorSourceUrl = null;
    var _colorSourceComponent = null;

    // A tinted logo with no visible source looks arbitrary, so the artwork it was sampled from is
    // shown beside it. A fixed color has no source, so nothing is shown.
    function updateColorSourceSample() {
        var row = view.querySelector('#logoColorSourceRow');
        if (!row) return;

        var source = COLOR_SOURCES[view.querySelector('#selectColorSource').value];
        if (!source) {
            row.classList.add('hidden');
            return;
        }

        row.classList.remove('hidden');
        view.querySelector('#logoColorSourceLabel').textContent = source.label;

        // The two demo images never change, so each is fetched once.
        if (_colorSourceComponent === source.component) return;
        _colorSourceComponent = source.component;

        shared.fetchObjectUrl('Preview/Component/' + source.component).then(function (objectUrl) {
            if (_colorSourceUrl) URL.revokeObjectURL(_colorSourceUrl);
            _colorSourceUrl = objectUrl;
            view.querySelector('#logoColorSourceImage').src = _colorSourceUrl;
        }).catch(function (error) {
            console.error('Failed to load the color source image:', error);
            row.classList.add('hidden');
            _colorSourceComponent = null;
        });
    }

    function updateVisibility() {
        var mode = view.querySelector('#selectSubtitleMode').value;
        var twoSizes = mode === 'TitleLarge' || mode === 'SubtitleLarge';
        view.querySelector('#secondarySizeContainer').style.display = twoSizes ? 'block' : 'none';

        // Sampling takes the color from the artwork, so leaving a swatch on screen would imply the
        // chosen color still paints the text. The control collapses to the opacity it does control.
        var sampling = view.querySelector('#selectColorSource').value !== 'Fixed';
        colorFields.forEach(function (handle) {
            if (handle.field.contains(view.querySelector('#txtLogoColor'))) handle.setOpacityOnly(sampling);
        });

        var colorLabel = view.querySelector('#txtLogoColor').closest('.inputContainer').querySelector('label[data-color-label]');
        if (colorLabel) {
            colorLabel.textContent = colorLabel.getAttribute(sampling ? 'data-opacity-label' : 'data-color-label');
        }

        var colorDesc = view.querySelector('#descLogoColor');
        if (colorDesc) {
            colorDesc.textContent = colorDesc.getAttribute(sampling ? 'data-opacity-desc' : 'data-color-desc');
        }

        updateColorSourceSample();

        // A frame fill has no color to pick, so the color controls step aside for it.
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

    // buildPreview
    // Draws the Base Preview Frame, whose Dark, Light, and Checkered backgrounds show the logo over each.
    function buildPreview() {
        _preview = createPreviewFrame({
            label: 'Logo Preview',
            delay: 400,
            render: renderPreview
        });
        view.querySelector('#logoPreviewHost').appendChild(_preview.element);
    }

    // renderPreview
    // Asks the server to draw the design on screen over the sample name.
    //
    // Output
    // [Promise<Blob|null>]: The rendered logo, or null before the designs load.
    function renderPreview() {
        if (!fullConfig) return Promise.resolve(null);
        saveCurrentSettings();
        var logo = getCurrentLogo();
        if (!logo) return Promise.resolve(null);

        // The sample name is a preview aid only, so it travels in the query rather than the design.
        var sample = view.querySelector('#txtSampleName').value.trim();
        var url = ApiClient.getUrl('Plugins/ArtworkGenerator/Preview/Logo', sample ? { name: sample } : undefined);

        return ApiClient.ajax({
            type: 'POST',
            url: url,
            data: JSON.stringify(logo.Settings),
            contentType: 'application/json'
        }).then(function (response) {
            if (!response.ok) throw new Error('Preview failed: ' + response.status);
            return response.blob();
        });
    }

    // schedulePreview
    // Renders the logo again once the fields stop changing.
    function schedulePreview() {
        if (_preview) _preview.update();
    }

    function onSettingChanged() {
        updateVisibility();
        checkDirty();
        schedulePreview();
    }

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

    // checkName
    // Says why a logo design name cannot be used, or nothing when it can.
    //
    // Inputs
    // name [string]: The trimmed name.
    // exceptId [string]: The design being renamed, which may keep its own name.
    //
    // Output
    // [string|null]: The reason, or null.
    function checkName(name, exceptId) {
        if (!name) return 'Enter a name.';
        return nameIsTaken(name, exceptId) ? 'A logo design with this name already exists.' : null;
    }

    function createNewLogo() {
        promptName('New Logo Design', '', 'Uppercase Outline', function (name) { return checkName(name); }).then(function (name) {
            if (!name) return;

            saveCurrentSettings();
            var source = getCurrentLogo();
            var created = {
                Id: generateGuid(),
                Name: name,
                Settings: Object.assign({}, logoSettingDefaults, source ? source.Settings : {})
            };

            logoDesigns.push(created);
            currentLogoId = created.Id;
            populateDropdown();
            checkDirty();
        });
    }

    function renameCurrentLogo() {
        var logo = getCurrentLogo();
        promptName('Rename Logo Design', logo.Name || '', '', function (name) { return checkName(name, logo.Id); }).then(function (name) {
            if (!name) return;

            logo.Name = name;
            populateDropdown();
            checkDirty();
        });
    }

    function deleteCurrentLogo() {
        if (logoDesigns.length <= 1) {
            shared.setStatus('logoStatus', 'Keep at least one logo design. Turn logos off in a profile instead.', true);
            return;
        }

        var logo = getCurrentLogo();
        var users = profilesUsingLogo(logo.Id).map(function (p) { return p.Name || 'Unnamed'; });
        var message = users.length
            ? 'Remove ' + (logo.Name || 'this logo design') + '? Its logos in ' + users.join(', ') + ' use the default design.'
            : 'Remove ' + (logo.Name || 'this logo design') + '?';

        confirmDialog(message, { title: 'Remove Logo Design', confirmText: 'Remove', destructive: true }).then(function (confirmed) {
            if (!confirmed) return;
            logoDesigns = logoDesigns.filter(function (l) { return l.Id !== logo.Id; });
            currentLogoId = null;
            populateDropdown();
            checkDirty();
        });
    }

    function saveConfig() {
        saveCurrentSettings();
        if (_saving) return;
        _saving = true;

        ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl('Plugins/ArtworkGenerator/Logos'),
            data: JSON.stringify(logoDesigns),
            contentType: 'application/json'
        }).then(function (response) {
            if (!response.ok) throw new Error('Save failed: ' + response.status);
            tracker.flashSaved();
            shared.setStatus('saveStatus', 'Logo designs saved.');
        }).catch(function (error) {
            console.error('Failed to save logo designs:', error);
            shared.errorText(error, 'Could not save the logo designs.').then(function (text) { shared.setStatus('saveStatus', text, true); });
        }).finally(function () {
            _saving = false;
        });
    }

    function bindEventListeners() {
        view.querySelector('#AgLogosForm').addEventListener('submit', function (e) {
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

        view.querySelectorAll('[data-setting]').forEach(function (el) {
            if (el.type === 'hidden') return;
            var evt = (el.type === 'checkbox' || el.tagName === 'SELECT') ? 'change' : 'input';
            el.addEventListener(evt, onSettingChanged);
        });
    }

    view.addEventListener('viewdestroy', function () {
        // The frame frees its own image. The color source image is released here.
        if (_preview) {
            _preview.destroy();
            _preview = null;
        }
        if (_colorSourceUrl) {
            URL.revokeObjectURL(_colorSourceUrl);
            _colorSourceUrl = null;
        }
    });
}
