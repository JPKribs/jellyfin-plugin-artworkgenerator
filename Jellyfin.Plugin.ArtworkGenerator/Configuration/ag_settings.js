// ag_settings.js
// The Settings tab, which holds the plugin wide options and Frame Extraction.

import { createArtworkPage, createArtworkSaveBar, disableHiddenFields, initCollapsibles } from './configurationpage?name=ag_shared.js';

export default function (view) {
    'use strict';

    var page = createArtworkPage(view, 3, {
        bind: bindPage,
        load: function () {
            loadFrameText().then(loadConfig);
        }
    });
    var shared = page.shared;

    // currentState
    // Serializes every field, so the change tracker can tell an edit from the last load or save.
    //
    // Output
    // [string]: The fields as JSON.
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

    // The Save Bar shows Unsaved changes while the fields differ from the last load or save.
    var tracker = shared.createChangeTracker({ serialize: currentState });
    page.guardUnsaved(tracker);
    var saveBar = null;

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

                var container = el.closest('.inputContainer, .checkboxContainer, .base-field, .ag-field');
                if (!container) return;

                var name = entry.label || entry.Label || '';
                var label = container.querySelector('span.checkboxLabel') || container.querySelector('label');
                if (label && name) {
                    label.textContent = name;
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

    // updateFrameVisibility
    // Shows the segment types and the letterbox thresholds only while their checkbox is on, and disables the hidden ones so Base's validation skips them.
    function updateFrameVisibility() {
        shared.bindReveal('chkAvoidMediaSegments', 'segmentTypes');
        shared.bindReveal('chkLetterboxDetection', 'letterboxSettings');
        disableHiddenFields(view.querySelector('#AgSettingsForm'));
    }

    function loadConfig() {
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
            saveBar.markClean();
        }).catch(function (error) {
            console.error('Failed to load config:', error);
            saveBar.setState('failed', 'Could not load the settings. Reload the page.');
        });
    }

    // savePluginSettings
    // Saves the plugin wide settings. The Save Bar has checked every field through Base's validation first.
    //
    // Output
    // [Promise]: Resolves once saved.
    function savePluginSettings() {
        // Read-modify-write: the poster configurations live in the same configuration object
        // and are edited on the other tab, so a blind overwrite here would discard them.
        return shared.getConfig().then(function (config) {
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
        }).catch(function (error) {
            console.error('Failed to save settings:', error);
            throw error;
        });
    }

    function bindPage() {
        initCollapsibles(view);
        saveBar = createArtworkSaveBar(view.querySelector('#AgSettingsForm'), view.querySelector('#saveBarHost'), tracker, {
            save: savePluginSettings,
            savedText: 'Settings saved.',
            failedText: 'Could not save the settings.'
        });

        view.querySelector('#txtImageChoiceCount').addEventListener('input', saveBar.check);
        view.querySelector('#txtImageCacheMinutes').addEventListener('input', saveBar.check);
        view.querySelector('#txtFixedSeed').addEventListener('input', saveBar.check);

        frameFields().forEach(function (el) {
            el.addEventListener(el.type === 'checkbox' || el.tagName === 'SELECT' ? 'change' : 'input', saveBar.check);
        });

        ['#chkAvoidMediaSegments', '#chkLetterboxDetection'].forEach(function (selector) {
            view.querySelector(selector).addEventListener('change', updateFrameVisibility);
        });
    }
}
