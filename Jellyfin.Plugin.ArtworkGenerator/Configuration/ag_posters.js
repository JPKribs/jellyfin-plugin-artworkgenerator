// ag_posters.js
// The Designs tab, which edits poster designs and previews them in both shapes.

import {
    bindColorField,
    createArtworkPage,
    createArtworkSaveBar,
    createList,
    createMediaCard,
    createPreviewFrame,
    confirmDialog,
    downloadJson,
    generateGuid,
    disableHiddenFields,
    initCollapsibles,
    openImageDialog,
    pickJsonFile,
    promptDialog,
    promptName
} from './configurationpage?name=ag_shared.js';

export default function (view) {
    'use strict';

    var fullConfig = null;
    var currentConfigId = null;
    var _componentObjectUrls = [];
    var _staticDataPromise = null;
    var _previews = {};
    var _samples = null;

    var page = createArtworkPage(view, 0, {
        bind: function () {
            initCollapsibles(view);
            buildSaveBar();
            buildPreviews();
            bindEventListeners();
            bindColorControls();
            loadPreviewComponents();
        },

        // Styles and fonts must be in the DOM before the first config is applied, otherwise a
        // stored value would look like an option that no longer exists.
        load: function () {
            loadStaticData().then(loadConfig);
        }
    });
    var shared = page.shared;

    var colorFields = [];

    // snapshot
    // Serializes the designs, so the change tracker can tell an edit from the last load or save.
    //
    // Output
    // [string]: The designs as JSON.
    function snapshot() {
        return JSON.stringify(fullConfig ? fullConfig.PosterConfigurations : null);
    }

    var tracker = shared.createChangeTracker({ serialize: snapshot });
    page.guardUnsaved(tracker);
    var saveBar = null;

    // buildSaveBar
    // Draws Base's Save Bar under the form.
    function buildSaveBar() {
        saveBar = createArtworkSaveBar(view.querySelector('#AgPostersForm'), view.querySelector('#saveBarHost'), tracker, {
            save: saveConfig,
            savedText: 'Designs saved.',
            failedText: 'Could not save the designs.'
        });
    }

    // checkDirty
    // Reads the fields into the design on screen and shows Unsaved changes while the designs differ from the last load or save.
    function checkDirty() {
        if (!fullConfig) return;
        saveCurrentConfigSettings();
        saveBar.check();
    }

    // bindColorControls
    // Draws each color setting with Base's Color Picker, and its opacity alone with Base's slider when the image gives the color.
    function bindColorControls() {
        view.querySelectorAll('[data-color-field]').forEach(function (field) {
            var handle = bindColorField(field, function () { checkDirty(); schedulePreview(); });
            handle.field = field;
            colorFields.push(handle);
        });
    }

    // syncColorControls
    // Redraws every color setting from its stored value.
    function syncColorControls() {
        colorFields.forEach(function (handle) { handle.sync(); });
    }

    function loadConfig() {
        shared.getConfig().then(function (config) {
            fullConfig = config;

            if (!config.PosterConfigurations || config.PosterConfigurations.length === 0) {
                config.PosterConfigurations = [{ Id: generateGuid(), Settings: {}, SeriesIds: [] }];
            }

            populateConfigDropdown();
            syncColorControls();
            saveCurrentConfigSettings();
            saveBar.markClean();
        }).catch(function (error) {
            console.error('Failed to load config:', error);
            saveBar.setState('failed', 'Could not load the designs. Reload the page.');
        });
    }

    function populateConfigDropdown() {
        var select = view.querySelector('#selectPosterConfig');
        var previousId = currentConfigId;
        select.innerHTML = '';

        var configs = fullConfig.PosterConfigurations.slice().sort(function (a, b) {
            if (a.IsDefault && !b.IsDefault) return -1;
            if (!a.IsDefault && b.IsDefault) return 1;
            return (a.Name || '').localeCompare(b.Name || '');
        });

        configs.forEach(function (config) {
            var option = document.createElement('option');
            option.value = config.Id;
            option.textContent = config.Name || 'Unnamed Design';
            select.appendChild(option);
        });

        if (previousId && configs.some(function (c) { return c.Id === previousId; })) {
            currentConfigId = previousId;
        } else {
            currentConfigId = configs[0].Id;
        }

        select.value = currentConfigId;
        loadCurrentConfig();
    }

    function loadCurrentConfig() {
        var config = getCurrentConfig();
        if (!config) return;
        var settings = config.Settings || {};

        view.querySelectorAll('[data-setting]').forEach(function (el) {
            var key = el.getAttribute('data-setting');
            var val = settings[key];

            if (el.type === 'checkbox') {
                // Checkboxes default to checked unless explicitly false, except those
                // flagged data-default-unchecked which default to off when unset.
                if (el.hasAttribute('data-default-unchecked')) {
                    el.checked = val === true;
                } else {
                    el.checked = val !== false;
                }
            } else if (el.getAttribute('data-type') === 'number') {
                el.value = val || 0;
            } else if (el.tagName === 'SELECT') {
                applySelectValue(el, val);
            } else {
                el.value = val || '';
            }
        });

        updateConfigActions();
        updateVisibility();
        updateStyleAvailability();
        updateStyleDescription();
        syncColorControls();
        schedulePreview();
    }

    function getCurrentConfig() {
        return fullConfig.PosterConfigurations.find(function (c) { return c.Id === currentConfigId; });
    }

    // Assigns a stored value to a <select>, falling back to the setting's declared default when
    // the value is missing or names an option that no longer exists.
    //
    // Assigning an unknown value to a <select> leaves it with selectedIndex -1 and an empty
    // string value, which would then be written back on save. The server cannot deserialize ""
    // into an enum, so the save would fail with a 400. This affects every setting added after a
    // config was last written, so it is handled here once for all selects rather than being
    // patched per setting. The default comes from data-default because for seven of these the
    // correct default is not the first option.
    function applySelectValue(el, val) {
        // The default comes from the server, which reads it off the settings model. The markup
        // attribute stays as a fallback for the moment before that call returns.
        var served = defaultFor(el.getAttribute('data-setting'));
        var fallback = served !== undefined && served !== null ? String(served) : el.getAttribute('data-default');

        if (val !== undefined && val !== null && val !== '') {
            el.value = val;
            if (el.selectedIndex !== -1) return;

            // The stored value is not offered any more — keep it selectable rather than
            // silently rewriting the user's setting (e.g. a font that is not installed here).
            var preserved = document.createElement('option');
            preserved.value = val;
            preserved.textContent = val + ' (unavailable)';
            el.insertBefore(preserved, el.firstChild);
            el.value = val;
            return;
        }

        if (fallback) {
            el.value = fallback;
            if (el.selectedIndex !== -1) return;
        }

        el.selectedIndex = 0;
    }

    // The default design cannot be renamed or deleted: profiles fall back to it.
    function updateConfigActions() {
        var config = getCurrentConfig();
        var isDefault = !!(config && config.IsDefault);
        view.querySelector('#btnDeleteConfig').classList.toggle('hidden', isDefault);
        view.querySelector('#btnRenameConfig').classList.toggle('hidden', isDefault);
    }

    // Profiles that point a poster slot at the given design.
    function profilesUsingDesign(designId) {
        return (fullConfig.Profiles || []).filter(function (p) {
            return (p.Slots || []).some(function (s) { return s.Slot !== 'Logo' && s.DesignId === designId; });
        });
    }

    // checkName
    // Says why a design name cannot be used, or nothing when it can.
    //
    // Inputs
    // name [string]: The trimmed name.
    // exceptId [string]: The design being renamed, which may keep its own name.
    //
    // Output
    // [string|null]: The reason, or null.
    function checkName(name, exceptId) {
        if (!name) return 'Enter a name.';
        if (name.toLowerCase() === 'default') return 'Default is reserved for the default design.';
        var taken = fullConfig.PosterConfigurations.some(function (c) {
            return c.Id !== exceptId && c.Name && c.Name.toLowerCase() === name.toLowerCase();
        });
        return taken ? 'A design with this name already exists.' : null;
    }

    function createNewConfig() {
        promptName('New Design', '', 'Bold Titles', function (name) { return checkName(name); }).then(function (name) {
            if (!name) return;

            var newConfig = {
                Id: generateGuid(),
                Name: name,
                Settings: Object.assign({}, posterSettingDefaults),
                SeriesIds: []
            };

            fullConfig.PosterConfigurations.push(newConfig);
            currentConfigId = newConfig.Id;
            populateConfigDropdown();
            checkDirty();
        });
    }

    function renameCurrentConfig() {
        var config = getCurrentConfig();
        if (config.IsDefault) return;

        promptName('Rename Design', config.Name || '', '', function (name) { return checkName(name, config.Id); }).then(function (name) {
            if (!name) return;

            config.Name = name;
            populateConfigDropdown();
            checkDirty();
        });
    }

    function deleteCurrentConfig() {
        var config = getCurrentConfig();
        if (config.IsDefault) return;

        var users = profilesUsingDesign(config.Id).map(function (p) { return p.Name || 'Unnamed'; });
        var message = users.length
            ? 'Remove ' + (config.Name || 'this design') + '? Its images in ' + users.join(', ') + ' use the default design.'
            : 'Remove ' + (config.Name || 'this design') + '?';

        confirmDialog(message, { title: 'Remove Design', confirmText: 'Remove', destructive: true }).then(function (confirmed) {
            if (!confirmed) return;
            fullConfig.PosterConfigurations = fullConfig.PosterConfigurations.filter(function (c) {
                return c.Id !== currentConfigId;
            });
            currentConfigId = fullConfig.PosterConfigurations[0].Id;
            populateConfigDropdown();
            checkDirty();
        });
    }

    function importCurrentConfig() {
        pickJsonFile().then(function (template) {
            if (!template) return;
            if (!template.settings) {
                shared.setStatus('designStatus', 'Could not import the file, since it holds no design settings.', true);
                return;
            }

            promptName('Import Design', template.name || 'Imported Design', '', function (name) { return checkName(name); }).then(function (name) {
                if (!name) return;

                fullConfig.PosterConfigurations.push({
                    Id: generateGuid(),
                    Name: name,
                    Settings: template.settings,
                    SeriesIds: [],
                    IsDefault: false
                });
                currentConfigId = fullConfig.PosterConfigurations[fullConfig.PosterConfigurations.length - 1].Id;
                populateConfigDropdown();
                checkDirty();

                var source = template.author ? ' by ' + template.author : '';
                shared.setStatus('designStatus', name + source + ' imported. Select Save to keep it.');
            });
        }).catch(function (error) {
            console.error('Import error:', error);
            shared.setStatus('designStatus', 'Could not import the file. Select a design template in JSON.', true);
        });
    }

    function exportCurrentConfig() {
        var config = getCurrentConfig();
        if (!config) return;

        promptDialog([
            { name: 'author', label: 'Author', placeholder: 'Your name', required: true },
            { name: 'description', label: 'Description', placeholder: 'Bold titles over a dark fade', required: true }
        ], { title: 'Export Design', submitText: 'Export' }).then(function (values) {
            if (!values) return;

            shared.getConfig().then(function (pluginConfig) {
                var ver = pluginConfig.Version;
                downloadJson(config.Name.replace(/[^a-z0-9\s]/gi, '').replace(/\s+/g, '_') + '_v' + ver + '.json', {
                    name: config.Name,
                    description: values.description.trim(),
                    author: values.author.trim(),
                    version: ver,
                    createdDate: new Date().toISOString(),
                    settings: config.Settings
                });

                shared.setStatus('designStatus', 'Design exported.');
            }).catch(function (error) {
                console.error('Failed to get plugin version:', error);
                shared.setStatus('designStatus', 'Could not export the design.', true);
            });
        });
    }

    function saveCurrentConfigSettings() {
        var config = getCurrentConfig();
        if (!config) return;
        if (!config.Settings) config.Settings = {};

        view.querySelectorAll('[data-setting]').forEach(function (el) {
            var key = el.getAttribute('data-setting');
            if (el.type === 'checkbox') {
                config.Settings[key] = el.checked;
            } else if (el.getAttribute('data-type') === 'number') {
                config.Settings[key] = parseFloat(el.value) || 0;
            } else {
                config.Settings[key] = el.value;
            }
        });
    }

    // saveConfig
    // Saves the designs. The Save Bar has checked every shown field through Base's validation first.
    //
    // Output
    // [Promise]: Resolves once saved.
    function saveConfig() {
        saveCurrentConfigSettings();

        // Read-modify-write: the plugin-wide settings on the other tab live in the same
        // configuration object, so only the poster configurations are overwritten here.
        return shared.getConfig().then(function (serverConfig) {
            serverConfig.PosterConfigurations = fullConfig.PosterConfigurations;
            return shared.saveConfig(serverConfig);
        }).catch(function (error) {
            console.error('Failed to save poster configurations:', error);
            throw error;
        });
    }

    var posterSettingOptions = {};
    var posterSettingDefaults = {};
    var posterSettingText = {};
    var posterStyleDescriptions = {};
    var posterStyleTextNotes = {};
    var posterStyleShapes = {};
    var posterStyleSettings = {};
    var posterStyleText = {};

    // Pull each style's description from the generators (Plugins/ArtworkGenerator/PosterStyles)
    function loadPosterStyles() {
        return ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl('Plugins/ArtworkGenerator/PosterStyles'),
            dataType: 'json'
        }).then(function (styles) {
            (styles || []).forEach(function (s) {
                posterStyleDescriptions[s.value] = s.description;
                posterStyleTextNotes[s.value] = {
                    primary: s.primaryDescription || '',
                    secondary: s.secondaryDescription || ''
                };
                posterStyleShapes[s.value] = { Portrait: s.portrait !== false, Landscape: s.landscape !== false };
                posterStyleSettings[s.value] = s.settings || {};
                posterStyleText[s.value] = s.text || {};
            });
            updateStyleAvailability();
            updateStyleDescription();
        });
    }

    // The server may serialize these keys in either case, so they are matched without regard to it.
    function lookup(map, key) {
        if (!map || !key) return undefined;
        if (map[key] !== undefined) return map[key];
        var wanted = key.toLowerCase();
        for (var k in map) {
            if (Object.prototype.hasOwnProperty.call(map, k) && k.toLowerCase() === wanted) return map[k];
        }
        return undefined;
    }

    function optionsFor(setting) { return lookup(posterSettingOptions, setting); }
    // A design may word a shared setting in its own terms, so its wording wins where it has one.
    function textFor(setting) {
        var styleEl = view.querySelector('#selectPosterStyle');
        var own = styleEl ? lookup(posterStyleText[styleEl.value] || {}, setting) : null;
        return own || lookup(posterSettingText, setting);
    }
    function defaultFor(setting) { return lookup(posterSettingDefaults, setting); }

    // Pull every setting's choices and starting value from the settings model itself
    // (Plugins/ArtworkGenerator/SettingOptions), so adding a value to an enum in C# offers it
    // here with no change to this page.
    function loadSettingOptions() {
        return ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl('Plugins/ArtworkGenerator/SettingOptions'),
            dataType: 'json'
        }).then(function (payload) {
            posterSettingOptions = (payload && (payload.options || payload.Options)) || {};
            posterSettingDefaults = (payload && (payload.defaults || payload.Defaults)) || {};
            posterSettingText = (payload && (payload.text || payload.Text)) || {};
            populateSettingOptions();
            applySettingText();
        }).catch(function (error) {
            console.error('Failed to load setting options:', error);
        });
    }

    // Each setting's label and help text come from the settings model, so the wording lives next to
    // the setting in C# rather than in this page. Only the label and description inside a setting's
    // own container are touched: section intros and notes that describe a row are not setting text.
    function applySettingText() {
        view.querySelectorAll('[data-setting]').forEach(function (el) {
            var text = textFor(el.getAttribute('data-setting'));
            if (!text) return;

            var container = el.closest('.inputContainer, .checkboxContainer, .base-field, .ag-field');
            if (!container) return;

            var label = container.querySelector('span.checkboxLabel') || container.querySelector('label');

            // The color fields swap their own wording between color and opacity, so they keep it.
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

    // Fills each bound <select> from the server's list. Font families are left alone: they have
    // their own loader below, which keeps the markup list as a fallback.
    function populateSettingOptions() {
        // data-options is for a select whose choices come from the server but whose value is not
        // part of the design, such as the preview picker; data-setting covers the design's own.
        view.querySelectorAll('select[data-setting], select[data-options]').forEach(function (select) {
            var options = optionsFor(select.getAttribute('data-options') || select.getAttribute('data-setting'));
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

    // Replace the font dropdowns with the families actually installed on the server. The old
    // hardcoded list was desktop fonts (Arial, Calibri, ...) that a container image does not
    // ship, so picking one silently rendered in the fallback face instead.
    function loadFontFamilies() {
        return ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl('Plugins/ArtworkGenerator/Fonts'),
            dataType: 'json'
        }).then(function (families) {
            if (!families || !families.length) return;

            view.querySelectorAll('#selectSecondaryFontFamily, #selectPrimaryFontFamily').forEach(function (select) {
                select.innerHTML = '';
                families.forEach(function (family) {
                    var option = document.createElement('option');
                    option.value = family;
                    option.textContent = family;
                    select.appendChild(option);
                });

                // Keep a sensible default selectable when the server has no Arial.
                if (families.indexOf(select.getAttribute('data-default')) === -1) {
                    select.setAttribute('data-default', families[0]);
                }
            });
        }).catch(function (error) {
            // Non-fatal: fall back to the built-in list already in the markup.
            console.error('Failed to load server font families:', error);
        });
    }

    // Static data shared by every config: loaded once, and always before the first config is
    // applied so a stored font or style is never treated as an unknown option.
    function loadStaticData() {
        if (!_staticDataPromise) {
            _staticDataPromise = Promise.all([loadSettingOptions(), loadPosterStyles(), loadFontFamilies()]);
        }
        return _staticDataPromise;
    }

    function updateStyleDescription() {
        var style = view.querySelector('#selectPosterStyle').value;
        var el = view.querySelector('#posterStyleDescription');
        if (el) el.textContent = posterStyleDescriptions[style] || '';

        // Each style says in its own words what its title and subtitle are, because where the
        // text lands is the one thing that changes between them.
        applySettingText();

        var notes = posterStyleTextNotes[style] || {};
        var primaryNote = view.querySelector('#primaryStyleNote');
        if (primaryNote) primaryNote.textContent = notes.primary || '';
        var secondaryNote = view.querySelector('#secondaryStyleNote');
        if (secondaryNote) secondaryNote.textContent = notes.secondary || '';

        updateStyleAvailability();
    }

    function styleSupportsShape(style, shape) {
        var shapes = posterStyleShapes[style];
        return !shapes || shapes[shape] !== false;
    }

    // A style that cannot lay out one of the shapes still produces that image, drawn with
    // Standard. The note says so rather than hiding the style, so a stored choice is never
    // silently rewritten.
    function updateStyleAvailability() {
        var style = view.querySelector('#selectPosterStyle').value;
        var note = view.querySelector('#posterShapeNote');
        if (!note) return;

        var missing = ['Landscape', 'Portrait'].filter(function (shape) {
            return !styleSupportsShape(style, shape);
        });

        note.textContent = missing.length
            ? 'This style cannot lay out ' + missing.join(' or ').toLowerCase() + ' images, so those are drawn with Standard instead.'
            : '';
        shared.setVisible(note, missing.length > 0);
    }

    // buildPreviews
    // Draws one Base Preview Frame for each shape. Each renders the design on screen through the preview route.
    function buildPreviews() {
        ['Landscape', 'Portrait'].forEach(function (shape) {
            _previews[shape] = createPreviewFrame({
                label: shape + ' Preview',
                delay: 500,
                backgrounds: false,
                render: function () { return renderPreviewShape(shape); }
            });
            view.querySelector('#posterPreview' + shape).appendChild(_previews[shape].element);
        });
    }

    // renderPreviewShape
    // Asks the server to draw the design on screen in one shape. The server adjusts the design for the shape the same way it does when it generates the real image.
    //
    // Inputs
    // shape [string]: Landscape or Portrait.
    //
    // Output
    // [Promise<Blob|null>]: The rendered image, or null before the designs load.
    function renderPreviewShape(shape) {
        if (!fullConfig) return Promise.resolve(null);
        saveCurrentConfigSettings();
        var config = getCurrentConfig();
        if (!config || !config.Settings) return Promise.resolve(null);

        var kindEl = view.querySelector('#selectPreviewKind');
        var kind = (kindEl && kindEl.value) || 'Series';

        // Without a dataType ApiClient.ajax resolves to the raw Response, so the JPEG can be read as a blob.
        return ApiClient.ajax({
            type: 'POST',
            url: ApiClient.getUrl('Plugins/ArtworkGenerator/Preview', { kind: kind, shape: shape }),
            data: JSON.stringify(config.Settings),
            contentType: 'application/json'
        }).then(function (response) {
            if (!response.ok) throw new Error('Preview failed: ' + response.status);
            return response.blob();
        });
    }

    // schedulePreview
    // Renders both shapes again once the fields stop changing.
    function schedulePreview() {
        Object.keys(_previews).forEach(function (shape) { _previews[shape].update(); });
    }

    // The sample images the preview is drawn from.
    var SAMPLES = [
        { component: 'canvas', title: 'Canvas Image' },
        { component: 'poster', title: 'Series Poster' },
        { component: 'logo', title: 'Logo' }
    ];

    // loadPreviewComponents
    // Lists the sample images as Base Media Cards. Each opens larger when selected.
    function loadPreviewComponents() {
        _samples = createList({
            items: SAMPLES,
            layout: 'rows',
            label: 'Sample Images',
            render: function (sample) {
                return createMediaCard({
                    shape: 'square',
                    icon: 'image',
                    title: sample.title,
                    // Fetched through ApiClient rather than assigned straight to the image, since a bare
                    // image request carries no auth header and the endpoint stays behind the admin policy.
                    image: function () {
                        return shared.fetchObjectUrl('Preview/Component/' + sample.component).then(function (objectUrl) {
                            _componentObjectUrls.push(objectUrl);
                            sample.url = objectUrl;
                            return objectUrl;
                        }, function (error) {
                            console.error('Failed to load preview component "' + sample.component + '":', error);
                            return '';
                        });
                    },
                    onClick: function () {
                        if (sample.url) openImageDialog(sample.title, sample.url);
                    }
                });
            }
        });
        view.querySelector('#previewSamples').appendChild(_samples.element);
    }

    // Which settings a style uses comes from the server, where each generator declares its own
    // rules, so a new style needs no change here. A required setting is switched on and its toggle
    // taken away; a hidden one is a setting the style ignores.
    function settingState(style, setting) {
        var rules = posterStyleSettings[style];
        return (rules && rules[setting]) || 'Optional';
    }

    // applyStyleRules
    // Runs before the dependency rules below, which read checkbox state to decide what to show.
    function applyStyleRules(posterStyle) {
        view.querySelectorAll('[data-setting]').forEach(function (el) {
            var state = settingState(posterStyle, el.getAttribute('data-setting'));
            var container = el.closest('.inputContainer, .checkboxContainer, .ag-field');

            if (el.type === 'checkbox' && state === 'Required') {
                el.checked = true;
            }

            if (container) {
                container.hidden = state !== 'Optional';
            }
        });

        // A group whose settings are all gone would otherwise leave an empty box behind.
        view.querySelectorAll('[data-style-group]').forEach(function (group) {
            var shown = group.querySelectorAll('[data-setting]');
            var anyVisible = Array.prototype.some.call(shown, function (el) {
                return settingState(posterStyle, el.getAttribute('data-setting')) === 'Optional';
            });
            group.hidden = !anyVisible;
        });
    }

    // updateVisibility
    // Shows each field only where it applies, through Base's bindReveal for a checkbox and setVisible for any other rule. The style's own rules hide a field with the hidden attribute, so the two never undo each other.
    function updateVisibility() {
        var posterStyle = view.querySelector('#selectPosterStyle').value;
        var posterFill = view.querySelector('#selectPosterFill').value;
        var canvasSource = view.querySelector('#selectCanvasSource').value || 'Extract';

        applyStyleRules(posterStyle);

        // The Original strategy keeps a landscape frame's own shape, so the landscape ratio does
        // nothing there and is taken off screen rather than left to be set and ignored.
        var landscapeRatioApplies = posterFill !== 'Original';
        shared.setVisible('landscapeRatioField', landscapeRatioApplies);
        view.querySelector('#ratioNote').textContent = landscapeRatioApplies
            ? 'The shape each image is cropped to, such as 16:9 for landscape and 2:3 for portrait.'
            : 'Portrait images always crop to the portrait ratio. Landscape images keep the frame\'s own shape.';

        shared.setVisible('gridOptions', canvasSource === 'Grid');

        // The second overlay color is the gradient's far end on most designs, so it only appears
        // once a gradient is chosen. A design that shapes its own overlay hides the gradient
        // entirely and uses both colors directly, so there it is always on show.
        var gradientOffered = settingState(posterStyle, 'OverlayGradient') !== 'Hidden';
        var secondaryOverlay = view.querySelector('#secondaryOverlay');
        shared.setVisible(secondaryOverlay, !gradientOffered || view.querySelector('#selectOverlayGradient').value !== 'None');

        // Nested styling says "this belongs to the control above it". With no gradient above
        // it, the second color stands on its own and the indent reads as a mistake.
        secondaryOverlay.classList.toggle('base-indent', gradientOffered);

        // The graphic's placement shows once there is a graphic to place.
        shared.setVisible('graphicOptions', view.querySelector('#txtGraphicPath').value.trim() !== '');

        shared.bindReveal('chkShowPrimary', 'primaryOptions');
        shared.bindReveal('chkPrimaryUseCustomFont', 'primaryFontPath');
        shared.bindReveal('chkPrimaryUseCustomFont', 'primaryFontPicker', true);
        shared.bindReveal('chkShowSecondary', 'secondaryOptions');
        shared.bindReveal('chkSecondaryUseCustomFont', 'secondaryFontPath');
        shared.bindReveal('chkSecondaryUseCustomFont', 'secondaryFontPicker', true);

        updatePaletteMode();
        disableHiddenFields(view.querySelector('#AgPostersForm'));
    }

    // With Palette-Derived Colors on, the renderer replaces the overlay RGB channels with the
    // dominant color sampled from each episode's image and keeps only the alpha. Leaving a color
    // swatch and an ARGB field on screen implies the chosen color still applies, so those are
    // dropped and the field becomes what it actually controls: opacity.
    function updatePaletteMode() {
        var paletteCb = view.querySelector('#chkPaletteDerivedColors');
        var on = !!(paletteCb && paletteCb.checked);

        colorFields.forEach(function (handle) {
            if (!handle.field.hasAttribute('data-palette-aware')) return;
            handle.setOpacityOnly(on);

            var container = handle.field.closest('.ag-field');
            if (!container) return;

            // A design may name the overlay for what it actually draws, such as the bloom or the
            // sash, and that wins over the generic wording when a color is being set.
            var field = container.querySelector('[data-setting]');
            var own = field ? textFor(field.getAttribute('data-setting')) : null;

            var label = container.querySelector('label[data-color-label]');
            if (label) {
                var key = on ? 'data-opacity-label' : 'data-color-label';
                var ownLabel = !on && own && (own.label || own.Label);
                if (ownLabel) {
                    label.textContent = ownLabel;
                } else if (label.getAttribute(key)) {
                    label.textContent = label.getAttribute(key);
                }
            }

            var desc = container.querySelector('.fieldDescription');
            if (desc) {
                var dkey = on ? 'data-opacity-desc' : 'data-color-desc';
                var ownDesc = !on && own && (own.description || own.Description);
                if (ownDesc) {
                    desc.textContent = ownDesc;
                } else if (desc.getAttribute(dkey)) {
                    desc.textContent = desc.getAttribute(dkey);
                }
            }
        });
    }

    function bindEventListeners() {
        // Config selector
        view.querySelector('#selectPosterConfig').addEventListener('change', function () {
            saveCurrentConfigSettings();
            currentConfigId = this.value;
            loadCurrentConfig();
            view.querySelector('.content-primary').scrollIntoView({ behavior: 'smooth' });
        });

        // Config action buttons
        view.querySelector('#btnNewConfig').addEventListener('click', createNewConfig);
        view.querySelector('#btnDeleteConfig').addEventListener('click', deleteCurrentConfig);
        view.querySelector('#btnRenameConfig').addEventListener('click', renameCurrentConfig);
        view.querySelector('#btnExportConfig').addEventListener('click', exportCurrentConfig);
        view.querySelector('#btnImportConfig').addEventListener('click', importCurrentConfig);

        // Preview subject
        view.querySelector('#selectPreviewKind').addEventListener('change', schedulePreview);

        // Controls that affect visibility
        var visibilityControls = [
            '#selectPosterStyle', '#chkShowPrimary', '#chkShowSecondary', '#selectCanvasSource',
            '#selectPosterFill', '#selectOverlayGradient',
            '#chkSecondaryUseCustomFont', '#chkPrimaryUseCustomFont', '#chkPaletteDerivedColors'
        ];
        visibilityControls.forEach(function (selector) {
            var el = view.querySelector(selector);
            if (el) {
                var evt = (el.type === 'checkbox' || el.tagName === 'SELECT') ? 'change' : 'input';
                el.addEventListener(evt, function () { updateVisibility(); checkDirty(); });
            }
        });

        // Graphic path also affects visibility
        view.querySelector('#txtGraphicPath').addEventListener('input', function () {
            updateVisibility();
            checkDirty();
        });

        // Style description updates
        view.querySelector('#selectPosterStyle').addEventListener('change', updateStyleDescription);

        // Track changes on all settings inputs and refresh the live preview
        view.querySelectorAll('[data-setting]').forEach(function (el) {
            var evt = (el.type === 'checkbox' || el.tagName === 'SELECT') ? 'change' : 'input';
            el.addEventListener(evt, function () {
                checkDirty();
                schedulePreview();
            });
        });
    }

    view.addEventListener('viewdestroy', function () {
        // The frames free their own images. The component thumbnails are released here.
        Object.keys(_previews).forEach(function (shape) { _previews[shape].destroy(); });
        _previews = {};
        if (_samples) _samples.destroy();
        _samples = null;
        _componentObjectUrls.forEach(URL.revokeObjectURL);
        _componentObjectUrls = [];
    });
}
