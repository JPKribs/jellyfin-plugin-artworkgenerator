// ag_settings.js
// The Settings tab, which holds the plugin wide options, frame extraction, and the uploaded fonts.

import {
    confirmDialog,
    createArtworkPage,
    startArtworkPage,
    createArtworkSaveBar,
    createFilePicker,
    createRecordList,
    disableHiddenFields,
    initCollapsibles,
    t,
    withLanguage
} from './configurationpage?name=ag_shared.js';

// The page's entry point, which builds the page once the strings have loaded.
export default function (view) {
    startArtworkPage(view, build);
}

// build
// Builds the page and wires it through createArtworkPage.
//
// Inputs
// view [HTMLElement]: The page element.
function build(view) {
    'use strict';

    var page = createArtworkPage(view, 3, {
        bind: bindPage,
        load: function () {
            loadFrameText().then(loadConfig);
            loadUploadedFonts();
        }
    });
    var shared = page.shared;

    // The most bytes one font may have, the same cap the server keeps.
    var MAX_FONT_BYTES = 20 * 1024 * 1024;
    var FONTS_URL = 'Plugins/ArtworkGenerator/Fonts/Uploaded';
    var fontPicker = null;
    var fontList = null;
    var fontBar = null;
    var fontTracker = shared.createChangeTracker({
        serialize: function () {
            return fontPicker ? fontPicker.getValue().map(function (file) { return file.name; }).join('|') : '';
        }
    });

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
            url: withLanguage('Plugins/ArtworkGenerator/SettingOptions'),
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
            saveBar.setState('failed', t.tr('ErrorLoadSettings'));
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

    // loadUploadedFonts
    // Draws one row per font in the fonts folder, with its family, style, file name, and Delete.
    //
    // Output
    // [Promise]: Resolves once the list is drawn.
    function loadUploadedFonts() {
        return ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl(FONTS_URL),
            dataType: 'json'
        }).then(function (fonts) {
            fontList.setItems((fonts || []).map(function (font) {
                return {
                    id: font.fileName,
                    title: font.family,
                    secondary: font.style,
                    tags: [{ label: font.fileName }],
                    aside: deleteButton(font)
                };
            }));
        }).catch(function (error) {
            console.error('Failed to load uploaded fonts:', error);
            fontBar.setState('failed', t.tr('ErrorLoadUploadedFonts'));
        });
    }

    // deleteButton
    // Builds the Delete row button for one uploaded font.
    //
    // Inputs
    // font [object]: The font, with fileName, family, and style.
    //
    // Output
    // [HTMLButtonElement]: The button.
    function deleteButton(font) {
        var button = document.createElement('button');
        button.type = 'button';
        button.className = 'base-row-btn';
        button.title = t.tr('Delete');
        button.setAttribute('aria-label', t.format('DeleteName', font.fileName));
        var icon = document.createElement('span');
        icon.className = 'material-icons';
        icon.setAttribute('aria-hidden', 'true');
        icon.textContent = 'delete';
        button.appendChild(icon);
        button.addEventListener('click', function () { deleteFont(font); });
        return button;
    }

    // deleteFont
    // Asks first, then deletes one font from the fonts folder and redraws the list.
    //
    // Inputs
    // font [object]: The font, with fileName, family, and style.
    function deleteFont(font) {
        confirmDialog(t.format('ConfirmDeleteFont', font.fileName, font.family, font.style), {
            title: t.tr('HeaderDeleteFont'),
            confirmText: t.tr('Delete'),
            destructive: true
        }).then(function (confirmed) {
            if (!confirmed) return;
            ApiClient.ajax({
                type: 'DELETE',
                url: withLanguage(FONTS_URL),
                data: JSON.stringify({ FileName: font.fileName }),
                contentType: 'application/json'
            }).then(function () {
                fontBar.setState('saved', t.tr('ToastFontDeleted'));
                return loadUploadedFonts();
            }, function (error) {
                return shared.errorText(error, t.tr('ErrorDeleteFont')).then(function (text) {
                    fontBar.setState('failed', text);
                });
            });
        });
    }

    // readBase64
    // Reads a picked file as base64, the form the upload route takes.
    //
    // Inputs
    // file [File]: The picked font.
    //
    // Output
    // [Promise<string>]: The file's bytes as base64.
    function readBase64(file) {
        return new Promise(function (resolve, reject) {
            var reader = new FileReader();
            reader.onload = function () {
                var url = String(reader.result || '');
                resolve(url.slice(url.indexOf(',') + 1));
            };
            reader.onerror = function () { reject(new Error(t.format('ErrorReadFile', file.name))); };
            reader.readAsDataURL(file);
        });
    }

    // uploadFonts
    // Uploads each picked font in turn, then redraws the list. A refused font rejects with the server's sentence for it.
    //
    // Output
    // [Promise]: Resolves once every font is stored.
    function uploadFonts() {
        var files = fontPicker.getValue();
        if (!files.length) return Promise.reject(t.tr('ErrorSelectFontFile'));

        var refused = [];
        return files.reduce(function (chain, file) {
            return chain.then(function () {
                return readBase64(file).then(function (data) {
                    return ApiClient.ajax({
                        type: 'POST',
                        url: withLanguage(FONTS_URL),
                        data: JSON.stringify({ FileName: file.name, Data: data }),
                        contentType: 'application/json'
                    });
                }).catch(function (error) {
                    return shared.errorText(error, t.tr('ErrorUploadFont')).then(function (text) {
                        refused.push(t.format('ErrorUploadFontNamed', file.name) + ' ' + text);
                    });
                });
            });
        }, Promise.resolve()).then(function () {
            fontPicker.clear();
            return loadUploadedFonts();
        }).then(function () {
            // A sentence, not an Error, so the Upload bar shows it as written.
            if (refused.length) throw refused.join(' ');
        });
    }

    // bindFonts
    // Draws the uploaded fonts list, the File Picker, and the Upload bar.
    function bindFonts() {
        fontList = createRecordList({
            label: t.tr('LabelUploadedFonts'),
            emptyText: t.tr('EmptyUploadedFonts')
        });
        view.querySelector('#uploadedFontsHost').appendChild(fontList.element);

        fontPicker = createFilePicker({
            accept: '.ttf,.otf',
            maxSize: MAX_FONT_BYTES,
            multiple: true,
            dropText: t.tr('HelpDropFonts'),
            onChange: function () { fontBar.check(); }
        });
        view.querySelector('#fontPickerHost').appendChild(fontPicker.element);

        fontBar = createArtworkSaveBar(view.querySelector('#AgFontsForm'), view.querySelector('#fontUploadBarHost'), fontTracker, {
            save: uploadFonts,
            saveText: t.tr('Upload'),
            pendingText: t.tr('StatusFontsPending'),
            savingText: t.tr('StatusUploading'),
            savedText: t.tr('ToastFontsUploaded'),
            failedText: t.tr('ErrorUploadFonts')
        });

        page.addCleanup(function () {
            fontPicker.destroy();
            fontList.destroy();
        });
    }

    function bindPage() {
        initCollapsibles(view);
        bindFonts();
        saveBar = createArtworkSaveBar(view.querySelector('#AgSettingsForm'), view.querySelector('#saveBarHost'), tracker, {
            save: savePluginSettings,
            savedText: t.tr('SettingsSaved'),
            failedText: t.tr('ErrorSaveSettings')
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
