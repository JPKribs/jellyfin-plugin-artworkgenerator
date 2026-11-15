// ag_shared.js
// The one module that imports Base, with the tab list and the page wrapper every Artwork Generator page uses.

import {
    createColorPicker,
    createConfigPage,
    createList,
    createMatrixGrid,
    createMediaCard,
    createMediaPicker,
    createPreviewFrame,
    createSaveBar,
    createSliderField,
    createSubTabs,
    confirmDialog,
    downloadJson,
    errorText,
    generateGuid,
    initCollapsibles,
    openDialog,
    pickJsonFile,
    promptDialog,
    validateAll
} from './configurationpage?name=ag_jpkribs_shared.js';

export {
    createList,
    createMatrixGrid,
    createMediaCard,
    createMediaPicker,
    createPreviewFrame,
    createSubTabs,
    confirmDialog,
    downloadJson,
    generateGuid,
    initCollapsibles,
    pickJsonFile,
    promptDialog
};

export var PLUGIN_ID = 'b8715e44-6b77-4c88-9c74-2b6f4c7b9a1e';

// TABS
// The dashboard tabs, in the order they show.
var TABS = [
    { href: 'configurationpage?name=ag_posters', name: 'Designs' },
    { href: 'configurationpage?name=ag_logos', name: 'Logos' },
    { href: 'configurationpage?name=ag_profiles', name: 'Profiles' },
    { href: 'configurationpage?name=ag_settings', name: 'Settings' }
];

// createArtworkPage
// Wires a page through Base's createConfigPage with this plugin's id, route prefix, and tabs.
//
// Inputs
// view [HTMLElement]: The page element.
// tabIndex [number]: The page's place in the tab list.
// opts [object]: The bind, load, and onHide callbacks createConfigPage takes.
//
// Output
// [object]: The page handle from createConfigPage.
export function createArtworkPage(view, tabIndex, opts) {
    return createConfigPage(view, Object.assign({
        pluginId: PLUGIN_ID,
        apiPrefix: 'Plugins/ArtworkGenerator',
        tabsKey: 'ag',
        tabIndex: tabIndex,
        tabs: TABS,
        leaveMessage: 'Leave without saving your changes?'
    }, opts));
}

// sameId
// Compares two GUIDs, ignoring case and dashes.
//
// Inputs
// a [string]: One id.
// b [string]: The other id.
//
// Output
// [boolean]: Whether they name the same item.
export function sameId(a, b) {
    return String(a || '').replace(/-/g, '').toLowerCase() === String(b || '').replace(/-/g, '').toLowerCase();
}

// promptName
// Asks for a record's name and checks it in the dialog before it closes.
//
// Inputs
// title [string]: The dialog title.
// value [string]: The name to start with.
// placeholder [string]: An example name.
// check [function]: Takes the trimmed name and returns an error sentence, or nothing when it is fine.
//
// Output
// [Promise<string|null>]: The trimmed name, or null when cancelled.
export function promptName(title, value, placeholder, check) {
    return promptDialog([{
        name: 'name',
        label: 'Name',
        value: value || '',
        placeholder: placeholder || '',
        required: true,
        validate: function (text) {
            return check ? check(String(text || '').trim()) : null;
        }
    }], { title: title }).then(function (values) {
        return values ? String(values.name || '').trim() : null;
    });
}

// openImageDialog
// Shows one image large in a Base dialog, drawn in a Base Preview Frame.
//
// Inputs
// title [string]: The dialog title, which also names the image.
// src [string]: The image address.
export function openImageDialog(title, src) {
    var frame = createPreviewFrame({
        label: title,
        backgrounds: false,
        zoom: false,
        render: function () { return src; }
    });
    openDialog({ title: title, body: frame.element, size: 'large', onClose: function () { frame.destroy(); } });
    frame.refresh();
}

// disableHiddenFields
// Disables every field a show or hide rule has taken off screen, and enables the rest, so Base's validation skips the hidden ones.
//
// Inputs
// root [ParentNode]: The form whose fields are checked.
export function disableHiddenFields(root) {
    root.querySelectorAll('input, select, textarea').forEach(function (el) {
        if (el.type === 'hidden') return;
        el.disabled = !!el.closest('.hidden, [hidden]');
    });
}

// revealInvalid
// Opens the collapsed section that holds the first field Base marked invalid, and moves focus to it.
//
// Inputs
// root [ParentNode]: The form that was checked.
function revealInvalid(root) {
    var field = root.querySelector('[aria-invalid="true"]');
    if (!field) return;
    var content = field.closest('.base-collapsible-content.collapsed');
    if (content) {
        var header = root.querySelector('.base-collapsible-header[data-target="' + content.id + '"]');
        if (header) header.click();
    }
    field.focus();
}

// createArtworkSaveBar
// Draws Base's Save Bar in a host, checks the form through Base's validation first, and keeps the bar in step with a change tracker.
//
// Inputs
// form [HTMLFormElement]: The form whose fields are checked before a save. Enter in a field saves too.
// host [HTMLElement]: Where the bar goes.
// tracker [object]: The page's change tracker from shared.createChangeTracker.
// opts [object]: save, a function returning a promise that rejects with a sentence or an error; savedText; failedText.
//
// Output
// [object]: The Save Bar handle, with check, which shows Unsaved changes while the tracker is dirty, and markClean, which clears it after a load.
export function createArtworkSaveBar(form, host, tracker, opts) {
    var bar = createSaveBar({
        fill: true,
        savedText: opts.savedText,
        onSave: function () {
            if (!validateAll(form)) {
                revealInvalid(form);
                return fail('Fix the fields marked in red.');
            }
            return Promise.resolve().then(opts.save).then(function () {
                tracker.markClean();
            }, fail);
        }
    });

    // The bar shows its own failed line when the promise rejects, so the reason replaces it once that has run.
    function fail(reason) {
        var text = typeof reason === 'string' ? Promise.resolve(reason) : errorText(reason, opts.failedText);
        return text.then(function (sentence) {
            setTimeout(function () { bar.setState('failed', sentence); }, 0);
            throw new Error(sentence);
        });
    }

    form.addEventListener('submit', function (e) {
        e.preventDefault();
        bar.save();
    });

    host.appendChild(bar.element);
    bar.check = function () { bar.setDirty(tracker.check()); };
    bar.markClean = function () { tracker.markClean(); bar.setDirty(false); };
    return bar;
}

// alphaPercent
// Reads the opacity of an ARGB color as a percent.
//
// Inputs
// value [string]: A color as #AARRGGBB or #RRGGBB.
//
// Output
// [number]: The opacity from 0 to 100.
export function alphaPercent(value) {
    var hex = String(value || '').replace('#', '');
    if (hex.length !== 8) return 100;
    return Math.round(parseInt(hex.substring(0, 2), 16) * 100 / 255);
}

// withAlpha
// Gives an ARGB color a new opacity and keeps its red, green, and blue.
//
// Inputs
// value [string]: A color as #AARRGGBB or #RRGGBB.
// percent [number]: The opacity from 0 to 100.
//
// Output
// [string]: The color as #AARRGGBB.
export function withAlpha(value, percent) {
    var hex = String(value || '').replace('#', '');
    var rgb = hex.length === 8 ? hex.substring(2) : (hex.length === 6 ? hex : 'FFFFFF');
    var alpha = Math.max(0, Math.min(255, Math.round(percent * 255 / 100)));
    return ('#' + alpha.toString(16).padStart(2, '0') + rgb).toUpperCase();
}

// bindColorField
// Draws one ARGB color setting with Base's Color Picker, and Base's slider for when only its opacity applies.
//
// Inputs
// field [HTMLElement]: The data-color-field element that holds the setting's hidden input.
// onChange [function]: Called after the reader changes the color or the opacity.
//
// Output
// [object]: A handle with sync, which redraws from the stored value, and setOpacityOnly, which shows the slider alone.
export function bindColorField(field, onChange) {
    var input = field.querySelector('input[data-setting]');
    var fallback = input.getAttribute('data-fallback') || '#FFFFFFFF';
    var label = field.closest('.inputContainer').querySelector('label[for="' + input.id + '"]');
    var quiet = false;

    function current() {
        return input.value || fallback;
    }

    var picker = createColorPicker({
        format: 'argb',
        value: current(),
        label: label ? label.textContent : 'Color',
        onChange: function (value) {
            if (quiet) return;
            input.value = String(value).toUpperCase();
            onChange();
        }
    });

    var opacity = createSliderField({
        min: 0,
        max: 100,
        suffix: '%',
        value: alphaPercent(current()),
        label: 'Opacity',
        onChange: function (percent) {
            if (quiet) return;
            input.value = withAlpha(current(), percent);
            quiet = true;
            picker.setValue(input.value);
            quiet = false;
            onChange();
        }
    });

    picker.input.id = input.id + 'Color';
    opacity.input.id = input.id + 'Opacity';

    var full = document.createElement('div');
    full.appendChild(picker.element);
    var only = document.createElement('div');
    only.className = 'hidden';
    only.appendChild(opacity.element);
    field.appendChild(full);
    field.appendChild(only);
    if (label) label.htmlFor = picker.input.id;

    return {
        sync: function () {
            quiet = true;
            picker.setValue(current());
            opacity.setValue(alphaPercent(current()));
            quiet = false;
        },
        setOpacityOnly: function (on) {
            full.classList.toggle('hidden', on);
            only.classList.toggle('hidden', !on);
            if (label) label.htmlFor = on ? opacity.input.id : picker.input.id;
        }
    };
}
