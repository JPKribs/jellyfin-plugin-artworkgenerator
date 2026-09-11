import { initCollapsibles, setTabs, createShared } from '/web/configurationpage?name=epg_jpkribs_shared.js';

export default function (view) {
    'use strict';

    var pluginId = 'b8715e44-6b77-4c88-9c74-2b6f4c7b9a1e';
    var shared = createShared(view, pluginId, 'Plugins/EpisodePosterGenerator');
    var fullConfig = null;
    var currentConfigId = null;
    var _initialized = false;
    var _dirty = false;
    var _savedConfigSnapshot = null;
    var _previewObjectUrl = null;
    var _componentObjectUrls = [];
    var _saving = false;
    var _staticDataPromise = null;

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
        var fallback = el.getAttribute('data-default');

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
                Settings: {
                    CanvasSource: 'Extract',
                    GenerateBackdrop: false,
                    EnableLetterboxDetection: true,
                    LetterboxBlackThreshold: 25,
                    LetterboxConfidence: 85.0,
                    ExtractWindowStart: 20.0,
                    ExtractWindowEnd: 80.0,
                    PosterStyle: 'Standard',
                    Shape: currentShape(),
                    CutoutType: 'Code',
                    CutoutBorder: true,
                    LogoPosition: 'Center',
                    LogoAlignment: 'Center',
                    LogoHeight: 30.0,
                    BrightenHDR: 25.0,
                    PosterFill: 'Original',
                    PosterDimensionRatio: '16:9',
                    PosterSafeArea: 5.0,
                    ElementSpacing: 2.0,
                    ShowEpisode: true,
                    EpisodeFontFamily: 'Arial',
                    EpisodeUseCustomFont: false,
                    EpisodeFontPath: '',
                    EpisodeFontStyle: 'Bold',
                    EpisodeFontSize: 7.0,
                    EpisodeFontColor: '#FFFFFFFF',
                    ShowTitle: true,
                    TitleFontFamily: 'Arial',
                    TitleUseCustomFont: false,
                    TitleFontPath: '',
                    TitleFontStyle: 'Bold',
                    TitleFontSize: 10.0,
                    TitleFontColor: '#FFFFFFFF',
                    LongTitleHandling: 'Ellipsis',
                    OverlayColor: '#66000000',
                    OverlayGradient: 'None',
                    OverlaySecondaryColor: '#66000000',
                    PaletteDerivedColors: false,
                    GraphicPath: '',
                    GraphicWidth: 25.0,
                    GraphicHeight: 25.0,
                    GraphicPosition: 'Center',
                    GraphicAlignment: 'Center'
                },
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

    var posterStyleDescriptions = {};
    var posterStyleShapes = {};

    // Pull each style's description from the generators (Plugins/EpisodePosterGenerator/PosterStyles)
    function loadPosterStyles() {
        return ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl('Plugins/EpisodePosterGenerator/PosterStyles'),
            dataType: 'json'
        }).then(function (styles) {
            (styles || []).forEach(function (s) {
                posterStyleDescriptions[s.value] = s.description;
                posterStyleShapes[s.value] = { Portrait: s.portrait !== false, Landscape: s.landscape !== false };
            });
            updateStyleAvailability();
            updateStyleDescription();
        });
    }

    // ── Fonts ───────────────────────────────────────────────

    // Replace the font dropdowns with the families actually installed on the server. The old
    // hardcoded list was desktop fonts (Arial, Calibri, ...) that a container image does not
    // ship, so picking one silently rendered in the fallback face instead.
    function loadFontFamilies() {
        return ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl('Plugins/EpisodePosterGenerator/Fonts'),
            dataType: 'json'
        }).then(function (families) {
            if (!families || !families.length) return;

            view.querySelectorAll('#selectEpisodeFontFamily, #selectTitleFontFamily').forEach(function (select) {
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
            _staticDataPromise = Promise.all([loadPosterStyles(), loadFontFamilies()]);
        }
        return _staticDataPromise;
    }

    function updateStyleDescription() {
        var style = view.querySelector('#selectPosterStyle').value;
        var el = view.querySelector('#posterStyleDescription');
        if (!el) return;

        var text = posterStyleDescriptions[style] || '';
        if (!styleSupportsShape(style, currentShape())) {
            text += (text ? ' ' : '') + 'This style cannot lay out ' + currentShape().toLowerCase() + ' posters, so Standard is drawn instead.';
        }
        el.textContent = text;
    }

    function currentShape() {
        var el = view.querySelector('#selectShape');
        return (el && el.value) || 'Landscape';
    }

    function styleSupportsShape(style, shape) {
        var shapes = posterStyleShapes[style];
        return !shapes || shapes[shape] !== false;
    }

    // Styles that cannot lay out the chosen shape are labelled rather than removed, so a stored
    // choice is never silently rewritten; the description explains the fallback.
    function updateStyleAvailability() {
        var shape = currentShape();
        view.querySelectorAll('#selectPosterStyle option').forEach(function (option) {
            var base = option.getAttribute('data-label') || option.textContent;
            option.setAttribute('data-label', base);
            option.textContent = styleSupportsShape(option.value, shape)
                ? base
                : base + ' (' + (shape === 'Portrait' ? 'landscape' : 'portrait') + ' only)';
        });

        var frame = view.querySelector('#posterPreviewFrame');
        if (frame) frame.classList.toggle('portrait', shape === 'Portrait');
    }

    // ── Live Preview ────────────────────────────────────────

    var _previewSeq = 0;

    function renderPreview() {
        if (!fullConfig) return;
        saveCurrentConfigSettings();
        var config = getCurrentConfig();
        if (!config || !config.Settings) return;

        var img = view.querySelector('#posterPreviewImage');
        var status = view.querySelector('#posterPreviewStatus');
        if (!img || !status) return;

        status.textContent = 'Rendering…';
        status.style.display = 'block';

        // Renders can overlap while the user is editing; only the latest request may
        // update the image so a slow older response can't overwrite a newer one.
        var seq = ++_previewSeq;

        // Use ApiClient.ajax so Jellyfin's auth headers are attached the same way the
        // working Configuration calls authenticate. Without a dataType it resolves to
        // the raw Response, so we can read the JPEG body as a blob.
        var kindEl = view.querySelector('#selectPreviewKind');
        var kind = kindEl ? kindEl.value : '';
        var url = ApiClient.getUrl('Plugins/EpisodePosterGenerator/Preview', kind ? { kind: kind } : undefined);

        ApiClient.ajax({
            type: 'POST',
            url: url,
            data: JSON.stringify(config.Settings),
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

            // Keep the enlarged modal in sync when it's open during live edits.
            var modal = view.querySelector('#previewModal');
            var modalImg = view.querySelector('#posterPreviewModalImage');
            if (modal && modalImg && modal.style.display !== 'none') {
                modalImg.src = _previewObjectUrl;
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
                url: ApiClient.getUrl('Plugins/EpisodePosterGenerator/Preview/Component/' + component)
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
    // For these, the checkbox is force-checked and its row hidden — single source of truth,
    // so the checkbox state and visibility can never disagree. This must run BEFORE the
    // dependency rules below, which read checkbox state to decide what to show.
    var forcedToggles = [
        { checkbox: 'chkShowEpisode', row: 'showEpisodeRow', styles: ['Cutout', 'Numeral', 'Brush', 'Timeline'] },
        { checkbox: 'chkShowTitle', row: 'showTitleRow', styles: ['Frame', 'Brush'] }
    ];

    function updateVisibility() {
        var posterStyle = view.querySelector('#selectPosterStyle').value;
        var posterFill = view.querySelector('#selectPosterFill').value;
        var canvasSourceEl = view.querySelector('#selectCanvasSource');
        var canvasSource = canvasSourceEl ? canvasSourceEl.value : 'Extract';

        function canvasOk(el) {
            var allowed = el.getAttribute('data-depends-on-canvas');
            return !allowed || allowed.split(',').includes(canvasSource);
        }

        // Apply forced toggles first so dependency rules see the corrected checkbox state.
        forcedToggles.forEach(function (rule) {
            var cb = view.querySelector('#' + rule.checkbox);
            var row = view.querySelector('#' + rule.row);
            var forced = rule.styles.includes(posterStyle);
            if (cb && forced) cb.checked = true;
            if (row) row.style.display = forced ? 'none' : 'block';
        });

        // Show/hide elements based on supported poster styles
        view.querySelectorAll('[data-poster-styles]').forEach(function (el) {
            el.style.display = el.getAttribute('data-poster-styles').split(',').includes(posterStyle) ? 'block' : 'none';
        });

        // Hide elements for specific fill modes
        view.querySelectorAll('[data-hide-for-posterfill]').forEach(function (el) {
            el.style.display = el.getAttribute('data-hide-for-posterfill').split(',').includes(posterFill) ? 'none' : 'block';
        });

        // Hide elements for specific styles
        view.querySelectorAll('[data-hide-for-styles]').forEach(function (el) {
            el.style.display = el.getAttribute('data-hide-for-styles').split(',').includes(posterStyle) ? 'none' : 'block';
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

            var hideForStyles = el.getAttribute('data-hide-for-styles');
            var hiddenByStyle = hideForStyles && hideForStyles.split(',').includes(posterStyle);
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
    // dominant color sampled from each episode's image and keeps only the alpha. Leaving a colour
    // swatch and an ARGB field on screen implies the chosen colour still applies, so those are
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
        view.querySelector('#EpgPostersForm').addEventListener('submit', function (e) {
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

        // Clicking the inline live preview opens it enlarged in the shared modal.
        var previewFrame = view.querySelector('#posterPreviewFrame');
        if (previewFrame) {
            previewFrame.addEventListener('click', function () {
                if (!_previewObjectUrl) return;
                openImageModal('previewModal', 'btnClosePreviewModal', 'posterPreviewModalImage',
                    _previewObjectUrl, 'Poster Preview');
            });
        }

        // Preview subject and shape
        view.querySelector('#selectPreviewKind').addEventListener('change', renderPreview);
        view.querySelector('#selectShape').addEventListener('change', function () {
            updateStyleAvailability();
            updateStyleDescription();
        });

        // Controls that affect visibility
        var visibilityControls = [
            '#selectPosterStyle', '#chkShowTitle', '#chkShowEpisode', '#selectCanvasSource',
            '#chkEnableLetterboxDetection', '#selectPosterFill', '#selectOverlayGradient',
            '#chkEpisodeUseCustomFont', '#chkTitleUseCustomFont', '#chkPaletteDerivedColors'
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
        setTabs('epg', 0, getTabs());

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
        if (_previewObjectUrl) {
            URL.revokeObjectURL(_previewObjectUrl);
            _previewObjectUrl = null;
        }
        _componentObjectUrls.forEach(URL.revokeObjectURL);
        _componentObjectUrls = [];
    });

    view.addEventListener('viewbeforehide', function (e) {
        window.removeEventListener('beforeunload', onBeforeUnload);

        if (_dirty) {
            var confirmed = confirm('You have unsaved changes. Are you sure you want to leave?');
            if (!confirmed) {
                e.preventDefault();
                setTabs('epg', 0, getTabs());
            }
        }
    });
}
