// ag_shared.js
// The one module that imports Base, with the tab list and the page wrapper every Artwork Generator page uses.

import {
    createCheckboxList,
    createColorPicker,
    createConfigPage,
    createPreviewFrame,
    createSliderField,
    confirmDialog,
    debounce,
    downloadJson,
    generateGuid,
    initCollapsibles,
    openDialog,
    pickJsonFile,
    promptDialog
} from './configurationpage?name=ag_jpkribs_shared.js';

export {
    createCheckboxList,
    createPreviewFrame,
    confirmDialog,
    debounce,
    downloadJson,
    generateGuid,
    initCollapsibles,
    openDialog,
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
// Shows one image large in a Base dialog.
//
// Inputs
// title [string]: The dialog title.
// src [string]: The image address.
// alt [string]: The image's alt text.
export function openImageDialog(title, src, alt) {
    var img = document.createElement('img');
    img.className = 'ag-dialog-image';
    img.src = src;
    img.alt = alt || title;
    openDialog({ title: title, body: img, size: 'large' });
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
// field [HTMLElement]: The ag-color-field element that holds the setting's hidden input.
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
