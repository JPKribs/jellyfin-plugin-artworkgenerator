import { initCollapsibles, setTabs, createShared, generateGuid } from '/web/configurationpage?name=ag_jpkribs_shared.js';

export default function (view) {
    'use strict';

    var pluginId = 'b8715e44-6b77-4c88-9c74-2b6f4c7b9a1e';
    var shared = createShared(view, pluginId, 'Plugins/ArtworkGenerator');
    var fullConfig = null;
    var currentConfigId = null;
    var _initialized = false;
    var _dirty = false;
    var _savedConfigSnapshot = null;
    var _previewUrls = { Landscape: null, Portrait: null };
    var _componentObjectUrls = [];
    var _saving = false;
    var _staticDataPromise = null;

    function getTabs() {
        return [
            { href: 'configurationpage?name=ag_posters', name: 'Designs' },
            { href: 'configurationpage?name=ag_logos', name: 'Logos' },
            { href: 'configurationpage?name=ag_profiles', name: 'Profiles' },
            { href: 'configurationpage?name=ag_settings', name: 'Settings' }
        ];
    }

    // ── Utilities ────────────────────────────────────────────

    function parseARGBHex(input) {
        if (!input) return null;
        input = input.replace('#', '').toUpperCase();
        if (input.length === 8) {
            return { rgb: '#' + input.substring(2), alpha: parseInt(input.substring(0, 2), 16) };
        }
        if (input.length === 6) {
            return { rgb: '#' + input, alpha: 255 };
        }
        if (input.length === 3) {
            var expanded = input[0] + input[0] + input[1] + input[1] + input[2] + input[2];
            return { rgb: '#' + expanded, alpha: 255 };
        }
        return null;
    }

    function trapFocus(container, e) {
        if (e.key !== 'Tab') return;
        var focusable = container.querySelectorAll(
            'button:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'
        );
        if (focusable.length === 0) return;
        var first = focusable[0];
        var last = focusable[focusable.length - 1];
        if (e.shiftKey && document.activeElement === first) {
            e.preventDefault();
            last.focus();
        } else if (!e.shiftKey && document.activeElement === last) {
            e.preventDefault();
            first.focus();
        }
    }

    function debounce(fn, delay) {
        var timer = null;
        return function () {
            var ctx = this, args = arguments;
            clearTimeout(timer);
            timer = setTimeout(function () { fn.apply(ctx, args); }, delay);
        };
    }

    // ── Unsaved Changes ─────────────────────────────────────

    function takeConfigSnapshot() {
        _savedConfigSnapshot = JSON.stringify(fullConfig.PosterConfigurations);
    }

    function markDirty() {
        if (!_dirty) {
            _dirty = true;
            var indicator = view.querySelector('#unsavedIndicator');
            if (indicator) indicator.classList.add('visible');
        }
    }

    function markClean() {
        _dirty = false;
        var indicator = view.querySelector('#unsavedIndicator');
        if (indicator) indicator.classList.remove('visible');
        takeConfigSnapshot();
    }

    function flashSaveSuccess() {
        var indicator = view.querySelector('#unsavedIndicator');
        if (!indicator) return;

        indicator.innerHTML = '';
        var dot = document.createElement('span');
        dot.className = 'jpk-unsaved-dot';
        dot.style.background = 'var(--ag-success-text)';
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

    function checkDirty() {
        if (!fullConfig || !_savedConfigSnapshot) return;
        saveCurrentConfigSettings();
        var current = JSON.stringify(fullConfig.PosterConfigurations);
        if (current !== _savedConfigSnapshot) {
            markDirty();
        } else {
            _dirty = false;
            var indicator = view.querySelector('#unsavedIndicator');
            if (indicator) indicator.classList.remove('visible');
        }
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
            input.type = field.type || 'text';
            input.id = 'inputModalField_' + index;
            input.value = field.value || '';
            if (field.placeholder) input.placeholder = field.placeholder;
            if (field.required) input.required = true;
            container.appendChild(input);

            if (field.description) {
                var desc = document.createElement('div');
                desc.className = 'fieldDescription';
                desc.textContent = field.description;
                container.appendChild(desc);
            }

            fieldsContainer.appendChild(container);
        });

        modal.style.display = 'flex';
        var firstInput = fieldsContainer.querySelector('input');
        if (firstInput) {
            setTimeout(function () { firstInput.focus(); firstInput.select(); }, 100);
        }

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
            fieldsContainer.querySelectorAll('input').forEach(function (input) {
                values.push(input.value);
            });
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

    function updateHexFromControls(container) {
        var rgb = container.querySelector('.color-picker').value.substring(1);
        var alpha = parseInt(container.querySelector('.alpha-slider').value);
        container.querySelector('.hex-input').value = '#' + alpha.toString(16).padStart(2, '0').toUpperCase() + rgb.toUpperCase();
    }

    function updateControlsFromHex(container) {
        var parsed = parseARGBHex(container.querySelector('.hex-input').value);
        if (parsed) {
            container.querySelector('.color-picker').value = parsed.rgb;
            container.querySelector('.alpha-slider').value = parsed.alpha;
            container.querySelector('.alpha-label').textContent = parsed.alpha;
        }
    }

    function bindColorControls() {
        view.querySelectorAll('.color-picker').forEach(function (picker) {
            var container = picker.parentElement;
            var slider = container.querySelector('.alpha-slider');
            var label = container.querySelector('.alpha-label');

            picker.addEventListener('input', function () { updateHexFromControls(container); checkDirty(); schedulePreview(); });
            slider.addEventListener('input', function () { label.textContent = slider.value; updateHexFromControls(container); checkDirty(); schedulePreview(); });
            container.querySelector('.hex-input').addEventListener('input', function () { updateControlsFromHex(container); checkDirty(); schedulePreview(); });
        });
    }

    function syncColorControls() {
        view.querySelectorAll('.color-picker').forEach(function (picker) {
            var container = picker.parentElement;
            var hex = container.querySelector('.hex-input').value;
            if (hex) {
                var parsed = parseARGBHex(hex);
                if (parsed) {
                    picker.value = parsed.rgb;
                    container.querySelector('.alpha-slider').value = parsed.alpha;
                    container.querySelector('.alpha-label').textContent = parsed.alpha;
                }
            }
        });
    }

    // ── Config Loading ──────────────────────────────────────

    function loadConfig() {
        Dashboard.showLoadingMsg();
        shared.getConfig().then(function (config) {
            fullConfig = config;

            if (!config.PosterConfigurations || config.PosterConfigurations.length === 0) {
                config.PosterConfigurations = [{ Id: generateGuid(), Settings: {}, SeriesIds: [] }];
            }

            populateConfigDropdown();
            syncColorControls();
            takeConfigSnapshot();
            markClean();
            Dashboard.hideLoadingMsg();
        }).catch(function (error) {
            console.error('Failed to load config:', error);
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Failed to load poster configurations. Please reload the page.', 'Error');
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
            option.textContent = config.Name || 'Unnamed Configuration';
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

    // ── Config Actions ──────────────────────────────────────

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

    // ── Config CRUD ─────────────────────────────────────────

    function createNewConfig() {
        showInputModal('New Configuration', [
            { label: 'Name', placeholder: 'My Poster Config', required: true }
        ], function (values) {
            if (!values) return;
            var name = values[0];

            if (!name || name.trim() === '') {
                Dashboard.alert('Configuration name is required.');
                return;
            }
            if (name.trim().toLowerCase() === 'default') {
                Dashboard.alert('The name "Default" is reserved and cannot be used.');
                return;
            }
            if (fullConfig.PosterConfigurations.some(function (c) {
                return c.Name && c.Name.toLowerCase() === name.trim().toLowerCase();
            })) {
                Dashboard.alert('A configuration with this name already exists.');
                return;
            }

            var newConfig = {
                Id: generateGuid(),
                Name: name.trim(),
                Settings: Object.assign({}, posterSettingDefaults),
                SeriesIds: []
            };

            fullConfig.PosterConfigurations.push(newConfig);
            currentConfigId = newConfig.Id;
            populateConfigDropdown();
            markDirty();
        });
    }

    function renameCurrentConfig() {
        var config = getCurrentConfig();
        if (config.IsDefault) {
            Dashboard.alert('The default configuration cannot be renamed.');
            return;
        }

        showInputModal('Rename Configuration', [
            { label: 'Name', value: config.Name || 'Unnamed', required: true }
        ], function (values) {
            if (!values) return;
            var newName = values[0];

            if (!newName || newName.trim() === '') return;
            if (newName.trim().toLowerCase() === 'default') {
                Dashboard.alert('The name "Default" is reserved and cannot be used.');
                return;
            }
            if (fullConfig.PosterConfigurations.some(function (c) {
                return c.Id !== config.Id && c.Name && c.Name.toLowerCase() === newName.trim().toLowerCase();
            })) {
                Dashboard.alert('A configuration with this name already exists.');
                return;
            }

            config.Name = newName.trim();
            populateConfigDropdown();
            markDirty();
        });
    }

    function deleteCurrentConfig() {
        var config = getCurrentConfig();
        if (config.IsDefault) {
            Dashboard.alert('Cannot delete the default configuration.');
            return;
        }

        var users = profilesUsingDesign(config.Id).map(function (p) { return p.Name || 'Unnamed'; });
        var message = users.length
            ? 'This design is used by: ' + users.join(', ') + '. Those images will use the default design for their shape instead. Delete it anyway?'
            : 'Are you sure you want to delete this design?';
        Dashboard.confirm(message, 'Delete Design', function (confirmed) {
            if (confirmed) {
                fullConfig.PosterConfigurations = fullConfig.PosterConfigurations.filter(function (c) {
                    return c.Id !== currentConfigId;
                });
                currentConfigId = fullConfig.PosterConfigurations[0].Id;
                populateConfigDropdown();
                markDirty();
            }
        });
    }

    function importCurrentConfig() {
        var fileInput = view.querySelector('#templateFileInput');

        fileInput.onchange = function (e) {
            var file = e.target.files[0];
            if (!file) return;

            var reader = new FileReader();
            reader.onload = function (event) {
                try {
                    var template = JSON.parse(event.target.result);
                    if (!template.settings) {
                        Dashboard.alert('Invalid template file: missing settings.');
                        return;
                    }

                    showInputModal('Import Configuration', [
                        { label: 'Name', value: template.name || 'Imported Configuration', required: true, description: 'Enter a name for the imported configuration.' }
                    ], function (values) {
                        if (!values) return;
                        var nameInput = values[0];

                        if (!nameInput || nameInput.trim() === '') return;
                        if (nameInput.trim().toLowerCase() === 'default') {
                            Dashboard.alert('The name "Default" is reserved and cannot be used.');
                            return;
                        }
                        if (fullConfig.PosterConfigurations.some(function (c) {
                            return c.Name && c.Name.toLowerCase() === nameInput.trim().toLowerCase();
                        })) {
                            Dashboard.alert('A configuration with this name already exists.');
                            return;
                        }

                        fullConfig.PosterConfigurations.push({
                            Id: generateGuid(),
                            Name: nameInput.trim(),
                            Settings: template.settings,
                            SeriesIds: [],
                            IsDefault: false
                        });
                        currentConfigId = fullConfig.PosterConfigurations[fullConfig.PosterConfigurations.length - 1].Id;
                        populateConfigDropdown();
                        markDirty();

                        Dashboard.alert('Template imported successfully! Version: ' + (template.version || 'unknown') +
                            (template.author ? '\nAuthor: ' + template.author : '') +
                            (template.description ? '\nDescription: ' + template.description : ''));
                    });
                } catch (error) {
                    console.error('Import error:', error);
                    Dashboard.alert('Failed to import template. Please ensure the file is a valid JSON template.');
                }
            };

            reader.readAsText(file);
            fileInput.value = '';
        };

        fileInput.click();
    }

    function exportCurrentConfig() {
        var config = getCurrentConfig();
        if (!config) {
            Dashboard.alert('No configuration selected.');
            return;
        }

        showInputModal('Export Configuration', [
            { label: 'Author', placeholder: 'Your name', required: true },
            { label: 'Description', placeholder: 'Brief description of this config', required: true }
        ], function (values) {
            if (!values) return;
            var author = values[0];
            var description = values[1];

            if (!author || !author.trim()) { Dashboard.alert('Author name is required.'); return; }
            if (!description || !description.trim()) { Dashboard.alert('Description is required.'); return; }

            Dashboard.showLoadingMsg();
            shared.getConfig().then(function (pluginConfig) {
                var ver = pluginConfig.Version;
                var json = JSON.stringify({
                    name: config.Name,
                    description: description.trim(),
                    author: author.trim(),
                    version: ver,
                    createdDate: new Date().toISOString(),
                    settings: config.Settings
                }, null, 2);

                var blob = new Blob([json], { type: 'application/json' });
                var url = URL.createObjectURL(blob);
                var a = document.createElement('a');
                a.href = url;
                a.download = config.Name.replace(/[^a-z0-9\s]/gi, '').replace(/\s+/g, '_') + '_v' + ver + '.json';
                document.body.appendChild(a);
                a.click();
                document.body.removeChild(a);
                URL.revokeObjectURL(url);

                Dashboard.hideLoadingMsg();
                Dashboard.alert('Template exported successfully!');
            }).catch(function (error) {
                console.error('Failed to get plugin version:', error);
                Dashboard.hideLoadingMsg();
                Dashboard.alert('Export failed. Please try again.');
            });
        });
    }

    // ── Save ────────────────────────────────────────────────

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

    function validateNumberInputs() {
        var errors = [];
        view.querySelectorAll('[data-setting][data-type="number"]').forEach(function (input) {
            if (input.offsetParent === null) return;

            var value = parseFloat(input.value);
            var min = parseFloat(input.getAttribute('min'));
            var max = parseFloat(input.getAttribute('max'));
            var labelEl = input.closest('.inputContainer');
            var labelText = labelEl ? labelEl.querySelector('label') : null;
            var name = labelText ? labelText.textContent.replace(':', '').trim() : input.id;

            if (isNaN(value)) {
                errors.push(name + ' must be a valid number.');
            } else {
                if (!isNaN(min) && value < min) errors.push(name + ' must be at least ' + min + '.');
                if (!isNaN(max) && value > max) errors.push(name + ' must be at most ' + max + '.');
            }
        });
        return errors;
    }

    function saveConfig() {
        saveCurrentConfigSettings();

        var validationErrors = validateNumberInputs();
        if (validationErrors.length > 0) {
            Dashboard.alert(validationErrors.join('\n'));
            return;
        }

        if (_saving) return;
        _saving = true;

        Dashboard.showLoadingMsg();

        // Read-modify-write: the plugin-wide settings on the other tab live in the same
        // configuration object, so only the poster configurations are overwritten here.
        shared.getConfig().then(function (serverConfig) {
            serverConfig.PosterConfigurations = fullConfig.PosterConfigurations;
            return shared.saveConfig(serverConfig);
        }).then(function (result) {
            markClean();
            flashSaveSuccess();
            Dashboard.processPluginConfigurationUpdateResult(result);
        }).catch(function (error) {
            // Without this the loading overlay would stay up forever on a failed save.
            console.error('Failed to save poster configurations:', error);
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Failed to save. Please check the server log and try again.', 'Error');
        }).finally(function () {
            _saving = false;
        });
    }

    // ── Style Descriptions ──────────────────────────────────

    var posterSettingOptions = {};
    var posterSettingDefaults = {};
    var posterSettingText = {};
    var posterStyleDescriptions = {};
    var posterStyleTextNotes = {};
    var posterStyleShapes = {};
    var posterStyleSettings = {};

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
    function textFor(setting) { return lookup(posterSettingText, setting); }
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

            var container = el.closest('.inputContainer, .checkboxContainer, .jpk-field');
            if (!container) return;

            var label = container.querySelector('span.checkboxLabel') || container.querySelector('label');

            // The color fields swap their own wording between color and opacity, so they keep it.
            if (label && !label.hasAttribute('data-color-label')) {
                var name = text.label || text.Label || '';
                label.textContent = el.type === 'checkbox' ? name : name + ':';
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

    // ── Fonts ───────────────────────────────────────────────

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
        note.style.display = missing.length ? 'block' : 'none';
    }

    // ── Live Preview ────────────────────────────────────────

    var _previewSeq = 0;

    function renderPreview() {
        if (!fullConfig) return;
        saveCurrentConfigSettings();
        var config = getCurrentConfig();
        if (!config || !config.Settings) return;

        // Renders can overlap while the user is editing; only the latest request may update an
        // image so a slow older response can't overwrite a newer one.
        var seq = ++_previewSeq;
        var kindEl = view.querySelector('#selectPreviewKind');
        var kind = (kindEl && kindEl.value) || 'Series';

        renderPreviewShape('Landscape', config.Settings, kind, seq);
        renderPreviewShape('Portrait', config.Settings, kind, seq);
    }

    // renderPreviewShape
    // Both previews come from the same settings: the server adjusts the design for the shape the
    // same way it does when it generates the real image.
    function renderPreviewShape(shape, settings, kind, seq) {
        var img = view.querySelector('#posterPreviewImage' + shape);
        var status = view.querySelector('#posterPreviewStatus' + shape);
        if (!img || !status) return;

        status.textContent = 'Rendering…';
        status.style.display = 'block';

        // Use ApiClient.ajax so Jellyfin's auth headers are attached the same way the
        // working Configuration calls authenticate. Without a dataType it resolves to
        // the raw Response, so we can read the JPEG body as a blob.
        var url = ApiClient.getUrl('Plugins/ArtworkGenerator/Preview', { kind: kind, shape: shape });

        ApiClient.ajax({
            type: 'POST',
            url: url,
            data: JSON.stringify(settings),
            contentType: 'application/json'
        }).then(function (response) {
            if (!response.ok) throw new Error('Preview failed: ' + response.status);
            return response.blob();
        }).then(function (blob) {
            if (seq !== _previewSeq) return;
            if (_previewUrls[shape]) URL.revokeObjectURL(_previewUrls[shape]);
            _previewUrls[shape] = URL.createObjectURL(blob);
            img.src = _previewUrls[shape];
            img.style.display = 'block';
            status.style.display = 'none';

            // Keep the enlarged modal in sync when it's open on this shape during live edits.
            var modal = view.querySelector('#previewModal');
            var modalImg = view.querySelector('#posterPreviewModalImage');
            if (modal && modalImg && modal.style.display !== 'none' && modal.getAttribute('data-shape') === shape) {
                modalImg.src = _previewUrls[shape];
            }
        }).catch(function (error) {
            if (seq !== _previewSeq) return;
            console.error('Poster preview error:', error);
            status.textContent = 'Preview unavailable';
            status.style.display = 'block';
        });
    }

    var schedulePreview = debounce(renderPreview, 500);

    // openImageModal
    // Opens one of the base jpk-dialog image modals with the given image and title.
    function openImageModal(modalId, closeButtonId, imageId, src, title) {
        var modal = view.querySelector('#' + modalId);
        var modalImg = view.querySelector('#' + imageId);
        if (!modal || !modalImg || !src) return;

        if (title) {
            var titleEl = modal.querySelector('.jpk-modal-title');
            if (titleEl) titleEl.textContent = title;
        }

        modalImg.src = src;
        modal.style.display = 'flex';

        function close() {
            modal.style.display = 'none';
            modal.removeEventListener('click', onBackdrop);
            document.removeEventListener('keydown', onKeydown);
        }
        function onBackdrop(e) { if (e.target === modal) close(); }
        function onKeydown(e) { if (e.key === 'Escape') { e.preventDefault(); close(); } }

        var btnClose = view.querySelector('#' + closeButtonId);
        if (btnClose) btnClose.onclick = close;
        modal.addEventListener('click', onBackdrop);
        document.addEventListener('keydown', onKeydown);
    }

    function loadPreviewComponents() {
        var componentTitles = {
            canvas: 'Canvas Image',
            poster: 'Series Poster',
            logo: 'Series Logo',
            graphic: 'Static Graphic'
        };

        // Fetched through ApiClient rather than assigned straight to img.src: a bare <img>
        // request carries no auth header, which is why this endpoint used to have to allow
        // anonymous access. Loading the bytes here keeps it behind the admin policy.
        view.querySelectorAll('.poster-component-img').forEach(function (img) {
            var component = img.getAttribute('data-component');
            var hide = function () {
                var wrapper = img.closest('.poster-component');
                if (wrapper) wrapper.style.display = 'none';
            };

            ApiClient.ajax({
                type: 'GET',
                url: ApiClient.getUrl('Plugins/ArtworkGenerator/Preview/Component/' + component)
            }).then(function (response) {
                if (!response.ok) throw new Error('Component request failed: ' + response.status);
                return response.blob();
            }).then(function (blob) {
                var objectUrl = URL.createObjectURL(blob);
                _componentObjectUrls.push(objectUrl);
                img.src = objectUrl;
            }).catch(function (error) {
                console.error('Failed to load preview component "' + component + '":', error);
                hide();
            });
        });

        view.querySelectorAll('.poster-component').forEach(function (comp) {
            comp.addEventListener('click', function () {
                var img = comp.querySelector('.poster-component-img');
                if (!img || !img.src) return;
                var component = comp.getAttribute('data-component');
                openImageModal('componentModal', 'btnCloseComponentModal', 'componentModalImage',
                    img.src, componentTitles[component] || 'Component');
            });
        });
    }

    // ── Visibility ──────────────────────────────────────────

    // Styles that REQUIRE a given toggle to be on (the style always renders that element).
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
            var container = el.closest('.inputContainer, .checkboxContainer');

            if (el.type === 'checkbox' && state === 'Required') {
                el.checked = true;
            }

            if (container) {
                container.hidden = state !== 'Optional';
            }
        });

        // A group whose settings are all gone would otherwise leave an empty box behind.
        view.querySelectorAll('.cutout-logo-group').forEach(function (group) {
            var shown = group.querySelectorAll('[data-setting]');
            var anyVisible = Array.prototype.some.call(shown, function (el) {
                return settingState(posterStyle, el.getAttribute('data-setting')) === 'Optional';
            });
            group.style.display = anyVisible ? 'block' : 'none';
        });
    }

    function updateVisibility() {
        var posterStyle = view.querySelector('#selectPosterStyle').value;
        var posterFill = view.querySelector('#selectPosterFill').value;
        var canvasSourceEl = view.querySelector('#selectCanvasSource');
        var canvasSource = canvasSourceEl ? canvasSourceEl.value : 'Extract';

        function canvasOk(el) {
            var allowed = el.getAttribute('data-depends-on-canvas');
            return !allowed || allowed.split(',').includes(canvasSource);
        }

        applyStyleRules(posterStyle);

        // The Original strategy keeps a landscape frame's own shape, so the landscape ratio does
        // nothing there and is taken off screen rather than left to be set and ignored.
        var landscapeRatioApplies = posterFill !== 'Original';
        var landscapeRatio = view.querySelector('#landscapeRatioField');
        if (landscapeRatio) {
            landscapeRatio.hidden = !landscapeRatioApplies;
        }

        var ratioNote = view.querySelector('#ratioNote');
        if (ratioNote) {
            ratioNote.textContent = landscapeRatioApplies
                ? 'The shape each image is cropped to, such as 16:9 for landscape and 2:3 for portrait.'
                : 'Portrait images always crop to the portrait ratio. Landscape images keep the frame\'s own shape.';
        }

        // Hide elements for specific fill modes
        view.querySelectorAll('[data-hide-for-posterfill]').forEach(function (el) {
            el.style.display = el.getAttribute('data-hide-for-posterfill').split(',').includes(posterFill) ? 'none' : 'block';
        });

        // Canvas source dependency (elements with only a canvas constraint)
        view.querySelectorAll('[data-depends-on-canvas]:not([data-depends-on])').forEach(function (el) {
            el.style.display = canvasOk(el) ? 'block' : 'none';
        });

        // Checkbox dependency chains (also honoring any canvas constraint)
        view.querySelectorAll('[data-depends-on]').forEach(function (el) {
            var deps = el.getAttribute('data-depends-on').split(',');
            var met = deps.every(function (id) {
                var dep = view.querySelector('#' + id.trim());
                return dep && dep.checked;
            });

            // A container the style rules already took away stays away.
            var hiddenByStyle = el.hidden || (el.querySelector('[data-setting]')
                && settingState(posterStyle, el.querySelector('[data-setting]').getAttribute('data-setting')) !== 'Optional');
            el.style.display = (met && !hiddenByStyle && canvasOk(el)) ? 'block' : 'none';
        });

        // Gradient dependency
        view.querySelectorAll('[data-depends-on-gradient]').forEach(function (el) {
            var select = view.querySelector('#' + el.getAttribute('data-depends-on-gradient'));
            el.style.display = (select && select.value !== 'None') ? 'block' : 'none';
        });

        // Value dependency (show when input has a value)
        view.querySelectorAll('[data-depends-on-value]').forEach(function (el) {
            var input = view.querySelector('#' + el.getAttribute('data-depends-on-value'));
            el.style.display = (input && input.value && input.value.trim() !== '') ? 'block' : 'none';
        });

        // Hide when checkbox is checked (inverse dependency)
        view.querySelectorAll('[data-hide-when-checked]').forEach(function (el) {
            var cb = view.querySelector('#' + el.getAttribute('data-hide-when-checked'));
            el.style.display = (cb && cb.checked) ? 'none' : 'block';
        });

        updatePaletteMode();
    }

    // With Palette-Derived Colors on, the renderer replaces the overlay RGB channels with the
    // dominant color sampled from each episode's image and keeps only the alpha. Leaving a color
    // swatch and an ARGB field on screen implies the chosen color still applies, so those are
    // dropped and the field becomes what it actually controls: opacity.
    function updatePaletteMode() {
        var paletteCb = view.querySelector('#chkPaletteDerivedColors');
        var on = !!(paletteCb && paletteCb.checked);

        view.querySelectorAll('[data-palette-aware]').forEach(function (group) {
            group.classList.toggle('palette-derived', on);

            var container = group.closest('.inputContainer');
            if (!container) return;

            var label = container.querySelector('.color-control-label');
            if (label) {
                var key = on ? 'data-opacity-label' : 'data-color-label';
                if (label.getAttribute(key)) label.textContent = label.getAttribute(key);
            }

            var desc = container.querySelector('.fieldDescription');
            if (desc) {
                var dkey = on ? 'data-opacity-desc' : 'data-color-desc';
                if (desc.getAttribute(dkey)) desc.textContent = desc.getAttribute(dkey);
            }
        });
    }

    // ── Event Binding ───────────────────────────────────────

    function bindEventListeners() {
        // Form save
        view.querySelector('#AgPostersForm').addEventListener('submit', function (e) {
            e.preventDefault();
            saveConfig();
        });

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

        // Clicking either live preview opens it enlarged in the shared modal.
        view.querySelectorAll('.poster-preview-frame').forEach(function (frame) {
            frame.addEventListener('click', function () {
                var shape = frame.getAttribute('data-shape');
                if (!_previewUrls[shape]) return;

                var modal = view.querySelector('#previewModal');
                if (modal) modal.setAttribute('data-shape', shape);
                openImageModal('previewModal', 'btnClosePreviewModal', 'posterPreviewModalImage',
                    _previewUrls[shape], shape + ' Preview');
            });
        });

        // Preview subject
        view.querySelector('#selectPreviewKind').addEventListener('change', renderPreview);

        // Controls that affect visibility
        var visibilityControls = [
            '#selectPosterStyle', '#chkShowPrimary', '#chkShowSecondary', '#selectCanvasSource',
            '#chkEnableLetterboxDetection', '#selectPosterFill', '#selectOverlayGradient',
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

    // ── Lifecycle ───────────────────────────────────────────

    function onBeforeUnload(e) {
        if (_dirty) {
            e.preventDefault();
            e.returnValue = '';
        }
    }

    view.addEventListener('viewshow', function () {
        setTabs('ag', 0, getTabs());

        if (!_initialized) {
            _initialized = true;
            initCollapsibles(view);
            bindEventListeners();
            bindColorControls();
            loadPreviewComponents();
        }

        window.addEventListener('beforeunload', onBeforeUnload);

        // Styles and fonts must be in the DOM before the first config is applied, otherwise a
        // stored value would look like an option that no longer exists.
        loadStaticData().then(loadConfig);
    });

    view.addEventListener('viewdestroy', function () {
        // Release the blobs backing the live preview and the component thumbnails.
        Object.keys(_previewUrls).forEach(function (shape) {
            if (_previewUrls[shape]) {
                URL.revokeObjectURL(_previewUrls[shape]);
                _previewUrls[shape] = null;
            }
        });
        _componentObjectUrls.forEach(URL.revokeObjectURL);
        _componentObjectUrls = [];
    });

    view.addEventListener('viewbeforehide', function (e) {
        window.removeEventListener('beforeunload', onBeforeUnload);

        if (_dirty) {
            var confirmed = confirm('You have unsaved changes. Are you sure you want to leave?');
            if (!confirmed) {
                e.preventDefault();
                setTabs('ag', 0, getTabs());
            }
        }
    });
}
