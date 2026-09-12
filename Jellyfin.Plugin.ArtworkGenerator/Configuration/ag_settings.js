import { setTabs, createShared } from '/web/configurationpage?name=ag_jpkribs_shared.js';

export default function (view) {
    'use strict';

    var pluginId = 'b8715e44-6b77-4c88-9c74-2b6f4c7b9a1e';
    var shared = createShared(view, pluginId, 'Plugins/ArtworkGenerator');
    var _initialized = false;
    var _saving = false;
    var _dirty = false;
    var _savedSnapshot = null;

    function getTabs() {
        return [
            { href: 'configurationpage?name=ag_posters', name: 'Designs' },
            { href: 'configurationpage?name=ag_logos', name: 'Logos' },
            { href: 'configurationpage?name=ag_profiles', name: 'Profiles' },
            { href: 'configurationpage?name=ag_settings', name: 'Settings' }
        ];
    }

    // ===== Unsaved Changes =====

    function currentState() {
        return JSON.stringify({
            ImageChoiceCount: view.querySelector('#txtImageChoiceCount').value,
            FixedExtractionSeed: view.querySelector('#txtFixedSeed').value,
            FrameExtraction: frameFields().map(function (el) {
                return el.type === 'checkbox' ? el.checked : el.value;
            }).join('|')
        });
    }

    function takeSnapshot() {
        _savedSnapshot = currentState();
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
        takeSnapshot();
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
        if (!_savedSnapshot) return;
        if (currentState() !== _savedSnapshot) {
            markDirty();
        } else {
            _dirty = false;
            var indicator = view.querySelector('#unsavedIndicator');
            if (indicator) indicator.classList.remove('visible');
        }
    }

    // ===== Config =====

    // The frame extraction fields carry the name of the property they set, so loading and saving
    // them is one loop rather than a line each.
    function frameFields() {
        return Array.prototype.slice.call(view.querySelectorAll('[data-frame-setting]'));
    }

    // Their labels and help text come from the settings model, like every other page.
    function loadFrameText() {
        return ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl('Plugins/ArtworkGenerator/SettingOptions'),
            dataType: 'json'
        }).then(function (payload) {
            var text = (payload && (payload.frameText || payload.FrameText)) || {};

            frameFields().forEach(function (el) {
                var entry = text[el.getAttribute('data-frame-setting')];
                if (!entry) return;

                var container = el.closest('.inputContainer, .checkboxContainer, .jpk-field');
                if (!container) return;

                var name = entry.label || entry.Label || '';
                var label = container.querySelector('span.checkboxLabel') || container.querySelector('label');
                if (label && name) {
                    label.textContent = el.type === 'checkbox' ? name : name + ':';
                }

                var description = container.querySelector('.fieldDescription');
                var help = entry.description || entry.Description || '';
                if (description && help) {
                    description.textContent = help;
                }
            });
        }).catch(function (error) {
            console.error('Failed to load frame extraction text:', error);
        });
    }

    // The letterbox thresholds only matter while detection is on.
    function updateFrameVisibility() {
        var on = view.querySelector('#chkLetterboxDetection');
        view.querySelectorAll('[data-depends-on="chkLetterboxDetection"]').forEach(function (el) {
            el.hidden = !(on && on.checked);
        });
    }

    function loadConfig() {
        Dashboard.showLoadingMsg();
        shared.getConfig().then(function (config) {
            view.querySelector('#txtImageChoiceCount').value = config.ImageChoiceCount || 3;
            view.querySelector('#txtFixedSeed').value = (config.FixedExtractionSeed === null || config.FixedExtractionSeed === undefined) ? '' : config.FixedExtractionSeed;

            var frame = config.FrameExtraction || {};
            frameFields().forEach(function (el) {
                var value = frame[el.getAttribute('data-frame-setting')];
                if (value === undefined || value === null) return;

                if (el.type === 'checkbox') {
                    el.checked = !!value;
                } else {
                    el.value = value;
                }
            });

            updateFrameVisibility();
            takeSnapshot();
            markClean();
            Dashboard.hideLoadingMsg();
        }).catch(function (error) {
            console.error('Failed to load config:', error);
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Failed to load settings. Please reload the page.', 'Error');
        });
    }

    function savePluginSettings() {
        if (_saving) return;
        _saving = true;

        Dashboard.showLoadingMsg();

        // Read-modify-write: the poster configurations live in the same configuration object
        // and are edited on the other tab, so a blind overwrite here would discard them.
        shared.getConfig().then(function (config) {

            var choices = parseInt(view.querySelector('#txtImageChoiceCount').value, 10);
            if (isNaN(choices)) choices = 3;
            config.ImageChoiceCount = Math.min(10, Math.max(1, choices));

            // Empty means random; anything else must be a whole number the server can store.
            var seedText = view.querySelector('#txtFixedSeed').value.trim();
            var seed = parseInt(seedText, 10);
            config.FixedExtractionSeed = seedText === '' || isNaN(seed) ? null : seed;

            config.FrameExtraction = config.FrameExtraction || {};
            frameFields().forEach(function (el) {
                var name = el.getAttribute('data-frame-setting');

                if (el.type === 'checkbox') {
                    config.FrameExtraction[name] = el.checked;
                    return;
                }

                var number = parseFloat(el.value);
                if (!isNaN(number)) {
                    config.FrameExtraction[name] = number;
                }
            });

            return shared.saveConfig(config);
        }).then(function (result) {
            markClean();
            flashSaveSuccess();
            Dashboard.processPluginConfigurationUpdateResult(result);
        }).catch(function (error) {
            console.error('Failed to save settings:', error);
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Failed to save settings. Please try again.', 'Error');
        }).finally(function () {
            _saving = false;
        });
    }

    // ===== Lifecycle =====

    function onBeforeUnload(e) {
        if (_dirty) {
            e.preventDefault();
            e.returnValue = '';
        }
    }

    view.addEventListener('viewshow', function () {
        setTabs('ag', 3, getTabs());

        if (!_initialized) {
            _initialized = true;
            view.querySelector('#btnSavePlugin').addEventListener('click', savePluginSettings);
            view.querySelector('#txtImageChoiceCount').addEventListener('input', checkDirty);
            view.querySelector('#txtFixedSeed').addEventListener('input', checkDirty);

            frameFields().forEach(function (el) {
                el.addEventListener(el.type === 'checkbox' ? 'change' : 'input', function () {
                    updateFrameVisibility();
                    checkDirty();
                });
            });
        }

        window.addEventListener('beforeunload', onBeforeUnload);
        loadFrameText();
        loadConfig();
    });

    view.addEventListener('viewbeforehide', function (e) {
        window.removeEventListener('beforeunload', onBeforeUnload);

        if (_dirty) {
            var confirmed = confirm('You have unsaved changes. Are you sure you want to leave?');
            if (!confirmed) {
                e.preventDefault();
                setTabs('ag', 3, getTabs());
            }
        }
    });
}
