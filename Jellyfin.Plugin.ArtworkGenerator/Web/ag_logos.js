// ag_logos.js
// The Logos tab, which edits text logo designs and previews them against a sample name.

import {
    bindColorField,
    createArtworkPage,
    createArtworkSaveBar,
    createMediaCard,
    createPreviewFrame,
    confirmDialog,
    disableHiddenFields,
    generateGuid,
    initCollapsibles,
    promptName,
    loadFontFamilies,
    sameId,
    t,
    withLanguage
} from './configurationpage?name=ag_shared.js';

export default function (view) {
    'use strict';

    var fullConfig = null;
    var logoDesigns = [];
    var currentLogoId = null;
    var _preview = null;
    var logoSettingText = {};
    var logoSettingOptions = {};
    var logoSettingDefaults = {};

    var page = createArtworkPage(view, 1, {
        bind: function () {
            initCollapsibles(view);
            buildSaveBar();
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

    var tracker = shared.createChangeTracker({ serialize: snapshot });
    page.guardUnsaved(tracker);
    var saveBar = null;

    // buildSaveBar
    // Draws Base's Save Bar under the form.
    function buildSaveBar() {
        saveBar = createArtworkSaveBar(view.querySelector('#AgLogosForm'), view.querySelector('#saveBarHost'), tracker, {
            save: saveConfig,
            savedText: t.tr('ToastLogoDesignsSaved'),
            failedText: t.tr('ErrorSaveLogoDesigns')
        });
    }

    // checkDirty
    // Reads the fields into the design on screen and shows Unsaved changes while the designs differ from the last load or save.
    function checkDirty() {
        if (!fullConfig) return;
        saveCurrentSettings();
        saveBar.check();
    }

    // bindColorControls
    // Draws each color setting with Base's Color Picker, which shows its opacity alone when the image gives the color.
    function bindColorControls() {
        view.querySelectorAll('[data-color-field]').forEach(function (field) {
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
            url: withLanguage('Plugins/ArtworkGenerator/SettingOptions'),
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
    // Font families are left alone: loadFontFamilies fills them from the uploaded, bundled, and server fonts.
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

            var container = el.closest('.inputContainer, .checkboxContainer, .base-field, .ag-field');
            if (!container) return;

            var label = container.querySelector('span.checkboxLabel') || container.querySelector('label');

            // The color field swaps its own wording between color and opacity, so it keeps it.
            if (label && !label.hasAttribute('data-str-opacity')) {
                var name = text.label || text.Label || '';
                label.textContent = name;
            }

            var description = container.querySelector('.fieldDescription');
            var help = text.description || text.Description || '';
            if (description && help && !description.hasAttribute('data-str-opacity')) {
                description.textContent = help;
            }
        });
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
        Promise.all([loadFontFamilies([view.querySelector('#selectLogoFontFamily')]), fetchLogos(), shared.getConfig(), loadSettingMetadata()]).then(function (results) {
            logoDesigns = results[1] || [];
            fullConfig = results[2] || {};
            fullConfig.Profiles = fullConfig.Profiles || [];

            // The server always supplies one; this only guards a malformed payload.
            if (logoDesigns.length === 0) {
                logoDesigns.push({ Id: generateGuid(), Name: 'Default', Settings: Object.assign({}, logoSettingDefaults) });
            }

            populateDropdown();
            saveCurrentSettings();
            saveBar.markClean();
        }).catch(function (error) {
            console.error('Failed to load logo designs:', error);
            saveBar.setState('failed', t.tr('ErrorLoadLogoDesigns'));
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
            option.textContent = logo.Name || t.tr('UnnamedLogo');
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
            preserved.textContent = t.format('OptionUnavailable', value);
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
        SeriesPoster: { component: 'poster', title: t.tr('LabelSeriesPoster'), subtitle: t.tr('HelpSampledFromPoster') },
        SeriesBackdrop: { component: 'canvas', title: t.tr('LabelSeriesBackdrop'), subtitle: t.tr('HelpSampledFromBackdrop') }
    };

    var _colorSourceUrls = {};
    var _colorSourceCard = null;

    // colorSourceImage
    // Fetches one demo image once, through ApiClient so the request carries the admin's auth.
    //
    // Inputs
    // component [string]: The demo image's name.
    //
    // Output
    // [Promise<string>]: Its object URL, or an empty string when it could not load.
    function colorSourceImage(component) {
        if (_colorSourceUrls[component]) return Promise.resolve(_colorSourceUrls[component]);
        return shared.fetchObjectUrl('Preview/Component/' + component).then(function (objectUrl) {
            _colorSourceUrls[component] = objectUrl;
            return objectUrl;
        }, function (error) {
            console.error('Failed to load the color source image:', error);
            return '';
        });
    }

    // updateColorSourceSample
    // A tinted logo with no visible source looks arbitrary, so a Base Media Card shows the artwork the color was sampled from. A fixed color or a photo fill has no source, so nothing is shown.
    function updateColorSourceSample() {
        var fill = view.querySelector('#selectLogoFill').value;
        var source = fill === 'Photo' ? null : COLOR_SOURCES[view.querySelector('#selectColorSource').value];
        shared.setVisible('logoColorSource', !!source);
        if (!source) return;

        var options = {
            shape: source.component === 'canvas' ? 'wide' : 'portrait',
            title: source.title,
            subtitle: source.subtitle,
            image: function () { return colorSourceImage(source.component); }
        };

        if (_colorSourceCard) {
            if (_colorSourceCard.getValue().title !== source.title) _colorSourceCard.setValue(options);
            return;
        }

        _colorSourceCard = createMediaCard(options);
        view.querySelector('#logoColorSource').appendChild(_colorSourceCard.element);
    }

    // updateVisibility
    // Shows each field only where it applies, through Base's bindReveal for a checkbox and setVisible for a choice.
    function updateVisibility() {
        var mode = view.querySelector('#selectSubtitleMode').value;
        shared.setVisible('secondarySizeContainer', mode === 'TitleLarge' || mode === 'SubtitleLarge');

        // Sampling takes the color from the artwork, so leaving a swatch on screen would imply the
        // chosen color still paints the text. The control collapses to the opacity it does control.
        var sampling = view.querySelector('#selectColorSource').value !== 'Fixed';
        colorFields.forEach(function (handle) {
            if (handle.field.contains(view.querySelector('#txtLogoColor'))) handle.setOpacityOnly(sampling);
        });

        var colorLabel = view.querySelector('#txtLogoColor').closest('.ag-field').querySelector('label[data-str-opacity]');
        if (colorLabel) {
            colorLabel.textContent = t.tr(colorLabel.getAttribute(sampling ? 'data-str-opacity' : 'data-str'));
        }

        var colorDesc = view.querySelector('#descLogoColor');
        if (colorDesc) {
            colorDesc.textContent = t.tr(colorDesc.getAttribute(sampling ? 'data-str-opacity' : 'data-str'));
        }

        // A frame fill has no color to pick, so the color controls step aside for it.
        shared.setVisible('logoColorOptions', view.querySelector('#selectLogoFill').value !== 'Photo');
        updateColorSourceSample();

        shared.bindReveal('chkLogoUseCustomFont', 'logoFontPath');
        shared.bindReveal('chkLogoUseCustomFont', 'logoFontPicker', true);
        shared.bindReveal('chkLogoOutline', 'logoOutlineOptions');

        disableHiddenFields(view.querySelector('#AgLogosForm'));
    }

    // buildPreview
    // Draws the Base Preview Frame, whose Dark, Light, and Checkered backgrounds show the logo over each.
    function buildPreview() {
        _preview = createPreviewFrame({
            label: t.tr('LabelLogoPreview'),
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
        var url = withLanguage('Plugins/ArtworkGenerator/Preview/Logo', sample ? { name: sample } : undefined);

        return ApiClient.ajax({
            type: 'POST',
            url: url,
            data: JSON.stringify(logo.Settings),
            contentType: 'application/json'
        }).then(function (response) {
            if (!response.ok) throw new Error(t.tr('ErrorLogoPreview'));
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
        if (!name) return t.tr('ErrorEnterName');
        return nameIsTaken(name, exceptId) ? t.tr('ErrorLogoDesignNameTaken') : null;
    }

    function createNewLogo() {
        promptName(t.tr('HeaderNewLogoDesign'), '', t.tr('PlaceholderLogoDesignName'), function (name) { return checkName(name); }).then(function (name) {
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
        promptName(t.tr('HeaderRenameLogoDesign'), logo.Name || '', '', function (name) { return checkName(name, logo.Id); }).then(function (name) {
            if (!name) return;

            logo.Name = name;
            populateDropdown();
            checkDirty();
        });
    }

    function deleteCurrentLogo() {
        if (logoDesigns.length <= 1) {
            shared.setStatus('logoStatus', t.tr('ErrorKeepOneLogoDesign'), true);
            return;
        }

        var logo = getCurrentLogo();
        var users = profilesUsingLogo(logo.Id).map(function (p) { return p.Name || t.tr('Unnamed'); });
        var name = logo.Name || t.tr('UnnamedLogo');
        var message = users.length
            ? t.format('ConfirmRemoveLogoDesignUsed', name, users.join(', '))
            : t.format('ConfirmRemoveName', name);

        confirmDialog(message, { title: t.tr('HeaderRemoveLogoDesign'), confirmText: t.tr('ButtonRemove'), destructive: true }).then(function (confirmed) {
            if (!confirmed) return;
            logoDesigns = logoDesigns.filter(function (l) { return l.Id !== logo.Id; });
            currentLogoId = null;
            populateDropdown();
            checkDirty();
        });
    }

    // saveConfig
    // Saves the logo designs to their own file. The Save Bar has checked every shown field through Base's validation first.
    //
    // Output
    // [Promise]: Resolves once saved.
    function saveConfig() {
        saveCurrentSettings();

        return ApiClient.ajax({
            type: 'POST',
            url: withLanguage('Plugins/ArtworkGenerator/Logos'),
            data: JSON.stringify(logoDesigns),
            contentType: 'application/json'
        }).then(function (response) {
            if (!response.ok) throw new Error(t.tr('ErrorSaveLogoDesigns'));
        }).catch(function (error) {
            console.error('Failed to save logo designs:', error);
            throw error;
        });
    }

    function bindEventListeners() {
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
        // The frame frees its own image. The color source images are released here.
        if (_preview) {
            _preview.destroy();
            _preview = null;
        }
        if (_colorSourceCard) {
            _colorSourceCard.destroy();
            _colorSourceCard = null;
        }
        Object.keys(_colorSourceUrls).forEach(function (key) { URL.revokeObjectURL(_colorSourceUrls[key]); });
        _colorSourceUrls = {};
    });
}
