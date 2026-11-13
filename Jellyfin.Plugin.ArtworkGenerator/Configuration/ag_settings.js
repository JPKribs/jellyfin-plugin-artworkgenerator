import { initCollapsibles, createConfigPage } from './configurationpage?name=ag_jpkribs_shared.js';

export default function (view) {
    'use strict';

    var pluginId = 'b8715e44-6b77-4c88-9c74-2b6f4c7b9a1e';
    var _saving = false;

    function getTabs() {
        return [
            { href: 'configurationpage?name=ag_posters', name: 'Designs' },
            { href: 'configurationpage?name=ag_logos', name: 'Logos' },
            { href: 'configurationpage?name=ag_profiles', name: 'Profiles' },
            { href: 'configurationpage?name=ag_settings', name: 'Settings' }
        ];
    }

    var page = createConfigPage(view, {
        pluginId: pluginId,
        apiPrefix: 'Plugins/ArtworkGenerator',
        tabsKey: 'ag',
        tabIndex: 3,
        tabs: getTabs(),
        leaveMessage: 'You have unsaved changes. Are you sure you want to leave?',
        bind: bindPage,
        load: function () {
            loadFrameText().then(loadConfig);
        }
    });
    var shared = page.shared;

    // ===== Unsaved Changes =====

    function currentState() {
        return JSON.stringify({
            ImageChoiceCount: view.querySelector('#txtImageChoiceCount').value,
            ImageCacheMinutes: view.querySelector('#txtImageCacheMinutes').value,
            FixedExtractionSeed: view.querySelector('#txtFixedSeed').value,
            FrameExtraction: frameFields().map(function (el) {
                return el.type === 'checkbox' ? el.checked : el.value;
            }).join('|')
        });
    }

    // Snapshots the fields; the indicator shows while they differ from the last load or save.
    var tracker = shared.createChangeTracker({ serialize: currentState, indicatorId: 'unsavedIndicator' });
    page.guardUnsaved(tracker);

    // The base flash keeps the "Unsaved changes" wording, so this one swaps in "Saved!" itself.
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
            var options = (payload && (payload.frameOptions || payload.FrameOptions)) || {};

            frameFields().forEach(function (el) {
                if (el.tagName !== 'SELECT') return;

                el.innerHTML = '';
                (options[el.getAttribute('data-frame-setting')] || []).forEach(function (choice) {
                    var option = document.createElement('option');
                    option.value = choice.value || choice.Value;
                    option.textContent = choice.label || choice.Label;
                    el.appendChild(option);
                });
            });

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

    // The letterbox thresholds and the segment types only matter while their checkbox is on.
    function updateFrameVisibility() {
        view.querySelectorAll('[data-depends-on]').forEach(function (el) {
            var on = view.querySelector('#' + el.getAttribute('data-depends-on'));
            el.hidden = !(on && on.checked);
        });
    }

    function loadConfig() {
        Dashboard.showLoadingMsg();
        shared.getConfig().then(function (config) {
            view.querySelector('#txtImageChoiceCount').value = config.ImageChoiceCount || 10;
            view.querySelector('#txtImageCacheMinutes').value = config.ImageCacheMinutes || 30;
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
            tracker.markClean();
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
            if (isNaN(choices)) choices = 10;
            config.ImageChoiceCount = Math.min(1000, Math.max(1, choices));

            var cacheMinutes = parseInt(view.querySelector('#txtImageCacheMinutes').value, 10);
            if (isNaN(cacheMinutes)) cacheMinutes = 30;
            config.ImageCacheMinutes = Math.min(1440, Math.max(5, cacheMinutes));

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

                if (el.tagName === 'SELECT') {
                    if (el.value) config.FrameExtraction[name] = el.value;
                    return;
                }

                var number = parseFloat(el.value);
                if (!isNaN(number)) {
                    config.FrameExtraction[name] = number;
                }
            });

            return shared.saveConfig(config);
        }).then(function (result) {
            tracker.markClean();
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

    function bindPage() {
        initCollapsibles(view);
        view.querySelector('#btnSavePlugin').addEventListener('click', savePluginSettings);
        view.querySelector('#txtImageChoiceCount').addEventListener('input', tracker.check);
        view.querySelector('#txtImageCacheMinutes').addEventListener('input', tracker.check);
        view.querySelector('#txtFixedSeed').addEventListener('input', tracker.check);

        frameFields().forEach(function (el) {
            el.addEventListener(el.type === 'checkbox' || el.tagName === 'SELECT' ? 'change' : 'input', function () {
                updateFrameVisibility();
                tracker.check();
            });
        });
    }
}
