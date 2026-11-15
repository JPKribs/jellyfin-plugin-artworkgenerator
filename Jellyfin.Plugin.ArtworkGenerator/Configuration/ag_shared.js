// ag_shared.js
// The one module that imports Base, with the tab list and the page wrapper every Artwork Generator page uses.

import {
    createCheckboxList,
    createConfigPage,
    createPreviewFrame,
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
