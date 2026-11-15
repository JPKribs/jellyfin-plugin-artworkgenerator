// ag_profiles.js
// The Profiles tab, which picks the images each kind of item gets and the items each profile applies to.

import {
    createArtworkPage,
    createCheckboxList,
    confirmDialog,
    generateGuid,
    initCollapsibles,
    openDialog,
    promptName,
    sameId
} from './configurationpage?name=ag_shared.js';

export default function (view) {
    'use strict';

    var fullConfig = null;
    var currentProfileId = null;
    var logoDesigns = [];
    var _saving = false;

    var EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

    var KINDS = [
        { kind: 'Series', label: 'Series', shapeKey: 'SeriesPrimaryShape', defaultShape: 'Portrait' },
        { kind: 'Season', label: 'Seasons', shapeKey: 'SeasonPrimaryShape', defaultShape: 'Portrait' },
        { kind: 'Episode', label: 'Episodes', shapeKey: 'EpisodePrimaryShape', defaultShape: 'Landscape' },
        { kind: 'Movie', label: 'Movies', shapeKey: 'MoviePrimaryShape', defaultShape: 'Portrait' },
        { kind: 'Video', label: 'Videos', shapeKey: 'VideoPrimaryShape', defaultShape: 'Portrait' },
        { kind: 'Collection', label: 'Collections', shapeKey: 'CollectionPrimaryShape', defaultShape: 'Portrait' },
        { kind: 'Folder', label: 'Folders', shapeKey: 'FolderPrimaryShape', defaultShape: 'Landscape' },
        { kind: 'Library', label: 'Libraries', shapeKey: 'LibraryPrimaryShape', defaultShape: 'Landscape' }
    ];

    // The image slots, in the order the matrix shows them as columns.
    var SLOTS = ['Primary', 'Thumb', 'Logo', 'Backdrop'];

    // Mirrors ArtworkProfile.SupportedSlots on the server: Jellyfin clients never show logos for
    // seasons or episodes, so those cells are not offered.
    var SUPPORTED = {
        Series: ['Primary', 'Thumb', 'Logo', 'Backdrop'],
        Season: ['Primary', 'Thumb', 'Backdrop'],
        Episode: ['Primary', 'Thumb', 'Backdrop'],
        Movie: ['Primary', 'Thumb', 'Logo', 'Backdrop'],
        Video: ['Primary', 'Thumb', 'Logo', 'Backdrop'],
        Collection: ['Primary', 'Thumb', 'Logo', 'Backdrop'],
        Folder: ['Primary', 'Thumb', 'Logo', 'Backdrop'],
        Library: ['Primary', 'Thumb']
    };

    var page = createArtworkPage(view, 2, {
        bind: function () {
            initCollapsibles(view);
            bindEventListeners();
        },
        load: loadConfig
    });
    var shared = page.shared;

    // snapshot
    // Serializes the profiles, so the change tracker can tell an edit from the last load or save.
    //
    // Output
    // [string]: The profiles as JSON.
    function snapshot() {
        return JSON.stringify(fullConfig ? fullConfig.Profiles : null);
    }

    var tracker = shared.createChangeTracker({ serialize: snapshot, indicatorId: 'unsavedIndicator', savedText: 'Saved' });
    page.guardUnsaved(tracker);

    // checkDirty
    // Shows the unsaved indicator while the profiles differ from the last load or save.
    function checkDirty() {
        if (fullConfig) tracker.check();
    }

    // The backdrop settings' labels and help text live on the settings model, the way the Designs
    // and Logos pages take theirs, so the wording is changed next to the setting itself.
    var backdropText = {};

    function loadBackdropText() {
        return ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl('Plugins/ArtworkGenerator/SettingOptions'),
            dataType: 'json'
        }).then(function (payload) {
            backdropText = (payload && (payload.backdropText || payload.BackdropText)) || {};
        }).catch(function (error) {
            console.error('Failed to load backdrop setting text:', error);
        });
    }

    function applyBackdropText() {
        view.querySelectorAll('[data-backdrop-setting]').forEach(function (el) {
            var text = backdropText[el.getAttribute('data-backdrop-setting')];
            if (!text) return;

            var container = el.closest('.inputContainer, .checkboxContainer');
            if (!container) return;

            var name = text.label || text.Label || '';
            var label = container.querySelector('span.checkboxLabel') || container.querySelector('label');
            if (label && name) {
                label.textContent = name;
            }

            var description = container.querySelector('.fieldDescription');
            var help = text.description || text.Description || '';
            if (description && help) {
                description.textContent = help;
            }
        });
    }

    // Logo designs are stored in their own file on the server, not in the plugin configuration.
    function fetchLogos() {
        return ApiClient.ajax({
            type: 'GET',
            url: ApiClient.getUrl('Plugins/ArtworkGenerator/Logos'),
            dataType: 'json'
        }).catch(function (error) {
            console.error('Failed to load logo designs:', error);
            return [];
        });
    }

    function loadConfig() {
        Promise.all([shared.getConfig(), loadAssignableItems(), fetchLogos(), loadBackdropText()]).then(function (results) {
            fullConfig = results[0];
            fullConfig.Profiles = fullConfig.Profiles || [];
            fullConfig.PosterConfigurations = fullConfig.PosterConfigurations || [];
            logoDesigns = results[2] || [];
            applyBackdropText();

            // The server always supplies a default profile; this only guards a malformed payload.
            if (!fullConfig.Profiles.some(function (p) { return p.IsDefault; })) {
                if (fullConfig.Profiles.length > 0) {
                    fullConfig.Profiles[0].IsDefault = true;
                } else {
                    fullConfig.Profiles.push({ Id: generateGuid(), Name: 'Default', IsDefault: true, SeriesIds: [], Slots: [], Backdrop: {} });
                }
            }

            populateProfileDropdown();
            tracker.markClean();
        }).catch(function (error) {
            console.error('Failed to load profiles:', error);
            shared.setStatus('saveStatus', 'Could not load the profiles. Reload the page.', true, { timeout: 0 });
        });
    }

    function sortedProfiles() {
        return fullConfig.Profiles.slice().sort(function (a, b) {
            if (a.IsDefault && !b.IsDefault) return -1;
            if (!a.IsDefault && b.IsDefault) return 1;
            return (a.Name || '').localeCompare(b.Name || '');
        });
    }

    function populateProfileDropdown() {
        var select = view.querySelector('#selectProfile');
        var profiles = sortedProfiles();
        select.innerHTML = '';

        profiles.forEach(function (profile) {
            var option = document.createElement('option');
            option.value = profile.Id;
            option.textContent = profile.Name || 'Unnamed Profile';
            select.appendChild(option);
        });

        if (!currentProfileId || !profiles.some(function (p) { return p.Id === currentProfileId; })) {
            currentProfileId = profiles[0].Id;
        }

        select.value = currentProfileId;
        loadCurrentProfile();
    }

    function getCurrentProfile() {
        return fullConfig.Profiles.find(function (p) { return p.Id === currentProfileId; });
    }

    function loadCurrentProfile() {
        var profile = getCurrentProfile();
        if (!profile) return;

        profile.Slots = profile.Slots || [];
        TARGETS.forEach(function (target) { assignedIds(profile, target); });
        profile.Backdrop = profile.Backdrop || {};

        var isDefault = !!profile.IsDefault;
        view.querySelector('#btnDeleteProfile').classList.toggle('hidden', isDefault);
        view.querySelector('#btnRenameProfile').classList.toggle('hidden', isDefault);
        view.querySelector('#assignmentSection').classList.toggle('hidden', isDefault);

        if (!isDefault) renderAssignments();
        renderMatrix();
        loadBackdropSettings();
    }

    function sortedDesigns() {
        return fullConfig.PosterConfigurations.slice().sort(function (a, b) {
            if (a.IsDefault && !b.IsDefault) return -1;
            if (!a.IsDefault && b.IsDefault) return 1;
            return (a.Name || '').localeCompare(b.Name || '');
        });
    }

    function findDesign(id) {
        return fullConfig.PosterConfigurations.find(function (d) { return sameId(d.Id, id); });
    }

    // Every design draws both shapes, so a slot may use any of them.
    function designOptions(currentId, deletedText) {
        var options = sortedDesigns().map(function (d) {
            return { value: d.Id, text: d.Name || 'Unnamed Design' };
        });

        if (!findDesign(currentId) && currentId && !sameId(currentId, EMPTY_GUID)) {
            options.unshift({ value: currentId, text: deletedText || 'Deleted design (uses the default)' });
        }

        return options;
    }

    // A secondary or tertiary design is optional, and a deleted one is skipped rather than
    // replaced by the default.
    function extraDesignOptions(currentId) {
        return [{ value: EMPTY_GUID, text: '\u2014' }].concat(designOptions(currentId, 'Deleted design (skipped)'));
    }

    function logoOptions(currentId) {
        var options = logoDesigns.map(function (l) { return { value: l.Id, text: l.Name || 'Unnamed Logo' }; });
        var exists = logoDesigns.some(function (l) { return sameId(l.Id, currentId); });
        if (!exists && currentId && !sameId(currentId, EMPTY_GUID)) {
            options.unshift({ value: currentId, text: 'Deleted logo (uses the default)' });
        }
        return options;
    }

    function getSlot(profile, kind, slot) {
        var assignment = profile.Slots.find(function (s) { return s.Kind === kind && s.Slot === slot; });
        if (!assignment) {
            assignment = { Kind: kind, Slot: slot, Enabled: false, DesignId: EMPTY_GUID, SecondaryDesignId: EMPTY_GUID, TertiaryDesignId: EMPTY_GUID };
            profile.Slots.push(assignment);
        }
        return assignment;
    }

    function makeSelect(options, value, label) {
        var select = document.createElement('select');
        select.className = 'emby-select ag-slot-select';
        if (label) select.setAttribute('aria-label', label);

        setOptions(select, options, value);
        return select;
    }

    function setOptions(select, options, value) {
        select.innerHTML = '';

        options.forEach(function (o) {
            var option = document.createElement('option');
            option.value = o.value;
            option.textContent = o.text;
            select.appendChild(option);
        });

        var match = options.find(function (o) { return sameId(o.value, value) || o.value === value; });
        if (match) select.value = match.value;
    }

    function renderMatrix() {
        var profile = getCurrentProfile();
        var container = view.querySelector('#slotMatrix');
        container.innerHTML = '';
        if (!profile) return;

        var table = document.createElement('table');
        var thead = document.createElement('thead');
        var headRow = document.createElement('tr');

        ['Item', 'Primary', 'Thumb', 'Logo', 'Backdrop'].forEach(function (text) {
            var th = document.createElement('th');
            th.scope = 'col';
            th.textContent = text;
            headRow.appendChild(th);
        });

        thead.appendChild(headRow);
        table.appendChild(thead);

        var tbody = document.createElement('tbody');
        KINDS.forEach(function (k) {
            var row = document.createElement('tr');
            var th = document.createElement('th');
            th.scope = 'row';
            th.textContent = k.label;
            row.appendChild(th);

            SLOTS.forEach(function (slot) {
                var td = document.createElement('td');
                if (SUPPORTED[k.kind].indexOf(slot) === -1) {
                    td.className = 'ag-slot-cell-unavailable';
                    td.textContent = '\u2014';
                    td.title = 'Jellyfin clients do not show this image for ' + k.label.toLowerCase() + '.';
                } else {
                    td.appendChild(buildCell(profile, k, slot));
                }
                row.appendChild(td);
            });

            tbody.appendChild(row);
        });

        table.appendChild(tbody);
        container.appendChild(table);
    }

    function buildCell(profile, k, slot) {
        var assignment = getSlot(profile, k.kind, slot);
        var cell = document.createElement('div');
        cell.className = 'ag-slot-cell';

        var toggle = document.createElement('label');
        toggle.className = 'ag-slot-toggle';
        var checkbox = document.createElement('input');
        checkbox.type = 'checkbox';
        checkbox.checked = !!assignment.Enabled;
        checkbox.setAttribute('aria-label', 'Generate ' + k.label.toLowerCase() + ' ' + slot.toLowerCase());
        toggle.appendChild(checkbox);
        toggle.appendChild(document.createTextNode(' Generate'));
        cell.appendChild(toggle);

        var controls = [];

        if (slot === 'Primary') {
            var shape = profile[k.shapeKey] || k.defaultShape;
            var shapeSelect = makeSelect([
                { value: 'Portrait', text: 'Portrait' },
                { value: 'Landscape', text: 'Landscape' }
            ], shape, k.label + ' primary shape');

            shapeSelect.addEventListener('change', function () {
                profile[k.shapeKey] = shapeSelect.value;
                checkDirty();
            });

            cell.appendChild(shapeSelect);
            controls.push(shapeSelect);
        }

        if (slot === 'Primary' || slot === 'Thumb') {
            var options = designOptions(assignment.DesignId);
            var designSelect = makeSelect(options, assignment.DesignId, k.label + ' ' + slot.toLowerCase() + ' design');

            if (sameId(assignment.DesignId, EMPTY_GUID) && options.length > 0) {
                assignment.DesignId = options[0].value;
            }

            designSelect.addEventListener('change', function () {
                assignment.DesignId = designSelect.value;
                checkDirty();
            });

            cell.appendChild(designSelect);
            controls.push(designSelect);

            // A second and third design only add choices to the Edit Images picker, drawn over the
            // same frames. The third is offered once there is a second, and clearing the second
            // moves the third up into its place, so the two never leave a gap.
            var secondarySelect = makeSelect([], null, k.label + ' ' + slot.toLowerCase() + ' second design');
            var tertiarySelect = makeSelect([], null, k.label + ' ' + slot.toLowerCase() + ' third design');

            var refreshExtras = function () {
                var second = assignment.SecondaryDesignId || EMPTY_GUID;
                var third = assignment.TertiaryDesignId || EMPTY_GUID;

                if (sameId(second, EMPTY_GUID) && !sameId(third, EMPTY_GUID)) {
                    second = assignment.SecondaryDesignId = third;
                    third = assignment.TertiaryDesignId = EMPTY_GUID;
                }

                setOptions(secondarySelect, extraDesignOptions(second), second);
                setOptions(tertiarySelect, extraDesignOptions(third), third);
                tertiarySelect.style.display = sameId(second, EMPTY_GUID) ? 'none' : '';
            };

            secondarySelect.addEventListener('change', function () {
                assignment.SecondaryDesignId = secondarySelect.value;
                refreshExtras();
                checkDirty();
            });

            tertiarySelect.addEventListener('change', function () {
                assignment.TertiaryDesignId = tertiarySelect.value;
                refreshExtras();
                checkDirty();
            });

            refreshExtras();
            cell.appendChild(secondarySelect);
            cell.appendChild(tertiarySelect);
            controls.push(secondarySelect, tertiarySelect);
        } else if (slot === 'Logo') {
            var logos = logoOptions(assignment.DesignId);
            var logoSelect = makeSelect(logos, assignment.DesignId, k.label + ' logo design');

            if (sameId(assignment.DesignId, EMPTY_GUID) && logos.length > 0) {
                assignment.DesignId = logos[0].value;
            }

            logoSelect.addEventListener('change', function () {
                assignment.DesignId = logoSelect.value;
                checkDirty();
            });

            cell.appendChild(logoSelect);
            controls.push(logoSelect);
        }

        function updateState() {
            controls.forEach(function (control) { control.disabled = !assignment.Enabled; });
            cell.classList.toggle('ag-slot-cell-off', !assignment.Enabled);
        }

        checkbox.addEventListener('change', function () {
            assignment.Enabled = checkbox.checked;
            updateState();
            checkDirty();
        });

        updateState();
        return cell;
    }

    var BACKDROP_DEFAULTS = {
        AspectRatio: '16:9'
    };

    function loadBackdropSettings() {
        var backdrop = getCurrentProfile().Backdrop;
        view.querySelectorAll('[data-backdrop-setting]').forEach(function (el) {
            var key = el.getAttribute('data-backdrop-setting');
            var value = backdrop[key];
            if (value === undefined || value === null) value = BACKDROP_DEFAULTS[key];

            if (el.type === 'checkbox') {
                el.checked = value !== false;
            } else {
                el.value = value;
            }
        });
    }

    function readBackdropSetting(el) {
        var profile = getCurrentProfile();
        if (!profile) return;

        var key = el.getAttribute('data-backdrop-setting');
        if (el.type === 'checkbox') {
            profile.Backdrop[key] = el.checked;
        } else if (el.getAttribute('data-type') === 'number') {
            var number = parseFloat(el.value);
            profile.Backdrop[key] = isNaN(number) ? BACKDROP_DEFAULTS[key] : number;
        } else {
            profile.Backdrop[key] = el.value.trim() || BACKDROP_DEFAULTS[key];
        }
    }

    // A profile draws every kind of item, so what it applies to is decided purely by assignment.
    // The assignable kinds differ only in which list they are stored in and what they are called,
    // so one set of functions serves them all rather than a near-identical copy for each.
    var TARGETS = [
        {
            key: 'SeriesIds',
            itemType: 'Series',
            noun: 'series',
            label: 'Assigned Series',
            button: 'Edit Series',
            modalTitle: 'Select Series',
            search: 'Search series',
            empty: 'No series assigned.',
            missing: 'No series found. Make sure you have TV series in your Jellyfin library.',
            items: []
        },
        {
            key: 'MovieIds',
            itemType: 'Movie',
            noun: 'movies',
            label: 'Assigned Movies',
            button: 'Edit Movies',
            modalTitle: 'Select Movies',
            search: 'Search movies',
            empty: 'No movies assigned.',
            missing: 'No movies found. Make sure you have movies in your Jellyfin library.',
            items: []
        },
        {
            key: 'VideoIds',

            // Music videos and home videos are both plain videos to Jellyfin, so one query covers
            // everything a library holds that is not part of a series and not a film.
            itemType: 'MusicVideo,Video',
            noun: 'videos',
            label: 'Assigned Videos',
            button: 'Edit Videos',
            modalTitle: 'Select Videos',
            search: 'Search videos',
            empty: 'No videos assigned.',
            missing: 'No videos found. Make sure you have music videos or home videos in your Jellyfin library.',
            items: []
        },
        {
            key: 'CollectionIds',
            itemType: 'BoxSet',
            noun: 'collections',
            label: 'Assigned Collections',
            button: 'Edit Collections',
            modalTitle: 'Select Collections',
            search: 'Search collections',
            empty: 'No collections assigned.',
            missing: 'No collections found. Make sure you have collections in your Jellyfin library.',
            items: []
        },
        {
            key: 'FolderIds',
            itemType: 'Folder',
            noun: 'folders',
            label: 'Assigned Folders',
            button: 'Edit Folders',
            modalTitle: 'Select Folders',
            search: 'Search folders',
            empty: 'No folders assigned.',
            missing: 'No folders found. Make sure you have folders in a mixed content or home videos library.',
            items: []
        },
        {
            key: 'LibraryIds',
            noun: 'libraries',
            label: 'Assigned Libraries',
            button: 'Edit Libraries',
            modalTitle: 'Select Libraries',
            search: 'Search libraries',
            empty: 'No libraries assigned.',
            missing: 'No libraries found.',
            load: function () {
                return ApiClient.getVirtualFolders().then(function (folders) {
                    return (folders || []).map(function (folder) {
                        return { Id: folder.ItemId, Name: folder.Name };
                    });
                });
            },
            items: []
        }
    ];

    function loadAssignableItems() {
        return Promise.all(TARGETS.map(function (target) {
            var request = target.load ? target.load() : ApiClient.getItems(ApiClient.getCurrentUserId(), {
                IncludeItemTypes: target.itemType,
                Recursive: true,
                SortBy: 'SortName',
                SortOrder: 'Ascending',
                Fields: 'Overview,ProductionYear'
            }).then(function (result) {
                return result.Items || [];
            });

            return request.then(function (items) {
                target.items = items;
            }).catch(function (error) {
                console.error('Failed to load ' + target.noun + ':', error);
                target.items = [];
            });
        }));
    }

    function assignedIds(profile, target) {
        profile[target.key] = profile[target.key] || [];
        return profile[target.key];
    }

    function renderAssignments() {
        var profile = getCurrentProfile();
        var host = view.querySelector('#assignmentBlocks');
        host.innerHTML = '';
        if (!profile) return;

        TARGETS.forEach(function (target) {
            host.appendChild(buildAssignmentBlock(profile, target));
        });
    }

    function buildAssignmentBlock(profile, target) {
        var block = document.createElement('div');
        block.className = 'inputContainer';

        var label = document.createElement('label');
        label.textContent = target.label;
        block.appendChild(label);

        var list = document.createElement('div');
        list.className = 'base-tags';
        block.appendChild(list);

        var ids = assignedIds(profile, target);
        if (ids.length === 0) {
            list.innerHTML = shared.emptySection(target.empty, { inline: true });
        } else {
            ids.forEach(function (id) {
                var item = target.items.find(function (candidate) { return sameId(candidate.Id, id); });
                if (!item) return;
                list.appendChild(buildAssignedTag(item, target, id));
            });
        }

        var button = document.createElement('button');
        button.setAttribute('is', 'emby-button');
        button.type = 'button';
        button.className = 'raised base-button secondary small';
        button.appendChild(document.createElement('span')).textContent = target.button;
        button.addEventListener('click', function () { showSelectionModal(target); });
        block.appendChild(button);

        return block;
    }

    // buildAssignedTag
    // Draws one assigned item as a Base tag with a remove button.
    //
    // Inputs
    // item [object]: The Jellyfin item.
    // target [object]: The assignable kind it belongs to.
    // id [string]: The stored id, removed when the tag is.
    //
    // Output
    // [HTMLElement]: The tag.
    function buildAssignedTag(item, target, id) {
        var tag = document.createElement('span');
        tag.className = 'base-tag';
        tag.appendChild(document.createTextNode(item.Name));

        var remove = document.createElement('button');
        remove.type = 'button';
        remove.className = 'base-tag-remove material-icons';
        remove.setAttribute('aria-label', 'Remove ' + item.Name);
        remove.textContent = 'close';
        remove.addEventListener('click', function () { removeAssignment(target, id); });

        tag.appendChild(remove);
        return tag;
    }

    // Ids claimed by another profile, so the same item cannot be assigned to two of them.
    function getAssignedElsewhere(target) {
        var ids = [];
        fullConfig.Profiles.forEach(function (p) {
            if (p.Id !== currentProfileId && !p.IsDefault && p[target.key]) {
                ids.push.apply(ids, p[target.key]);
            }
        });
        return ids;
    }

    // showSelectionModal
    // Opens a Base dialog holding a Checkbox List of every item of one kind. Items another profile holds are shown but cannot be picked.
    //
    // Inputs
    // target [object]: The assignable kind to pick from.
    function showSelectionModal(target) {
        if (!target.items || target.items.length === 0) {
            shared.setStatus('assignmentStatus', target.missing, true);
            return;
        }

        var elsewhere = getAssignedElsewhere(target);
        var current = assignedIds(getCurrentProfile(), target).filter(function (id) {
            return !elsewhere.some(function (other) { return sameId(other, id); });
        });

        var list = createCheckboxList({
            items: target.items,
            idKey: 'Id',
            matchKey: function (id) { return String(id || '').replace(/-/g, '').toLowerCase(); },
            value: current,
            disabledIds: elsewhere,
            placeholder: target.search,
            searchKeys: ['Name'],
            emptyMessage: 'No ' + target.noun + ' match.',
            renderRow: function (item) {
                var taken = elsewhere.some(function (other) { return sameId(other, item.Id); });
                return {
                    label: item.Name,
                    sublabel: item.ProductionYear ? String(item.ProductionYear) : null,
                    imageUrl: ApiClient.getImageUrl(item.Id, { type: 'Primary', maxWidth: 80, quality: 80 }),
                    note: taken ? 'In another profile' : null
                };
            }
        });

        openDialog({
            title: target.modalTitle,
            body: list.element,
            buttons: [
                { text: 'Cancel', value: null },
                {
                    text: 'Done',
                    className: 'base-button primary',
                    action: function (close) {
                        getCurrentProfile()[target.key] = list.getValue();
                        renderAssignments();
                        checkDirty();
                        close(true);
                    }
                }
            ],
            onClose: function () { list.destroy(); }
        });
    }

    function removeAssignment(target, id) {
        var profile = getCurrentProfile();
        profile[target.key] = assignedIds(profile, target).filter(function (existing) { return !sameId(existing, id); });
        renderAssignments();
        checkDirty();
    }

    function nameIsTaken(name, exceptId) {
        return fullConfig.Profiles.some(function (p) {
            return p.Id !== exceptId && p.Name && p.Name.toLowerCase() === name.toLowerCase();
        });
    }

    // checkName
    // Says why a profile name cannot be used, or nothing when it can.
    //
    // Inputs
    // name [string]: The trimmed name.
    // exceptId [string]: The profile being renamed, which may keep its own name.
    //
    // Output
    // [string|null]: The reason, or null.
    function checkName(name, exceptId) {
        if (!name) return 'Enter a name.';
        if (name.toLowerCase() === 'default') return 'Default is reserved for the default profile.';
        if (nameIsTaken(name, exceptId)) return 'A profile with this name already exists.';
        return null;
    }

    function createNewProfile() {
        promptName('New Profile', '', 'Anime', function (name) { return checkName(name); }).then(function (name) {
            if (!name) return;

            // Start from a copy of the profile on screen, so a new profile only needs its differences.
            var source = getCurrentProfile();
            var copy = JSON.parse(JSON.stringify(source));
            copy.Id = generateGuid();
            copy.Name = name;
            copy.IsDefault = false;
            copy.SeriesIds = [];

            fullConfig.Profiles.push(copy);
            currentProfileId = copy.Id;
            populateProfileDropdown();
            checkDirty();
        });
    }

    function renameCurrentProfile() {
        var profile = getCurrentProfile();
        if (profile.IsDefault) return;

        promptName('Rename Profile', profile.Name || '', '', function (name) { return checkName(name, profile.Id); }).then(function (name) {
            if (!name) return;

            profile.Name = name;
            populateProfileDropdown();
            checkDirty();
        });
    }

    function deleteCurrentProfile() {
        var profile = getCurrentProfile();
        if (profile.IsDefault) return;

        confirmDialog('Remove ' + (profile.Name || 'this profile') + '? Its assigned items use the default profile.', {
            title: 'Remove Profile',
            confirmText: 'Remove',
            destructive: true
        }).then(function (confirmed) {
            if (!confirmed) return;
            fullConfig.Profiles = fullConfig.Profiles.filter(function (p) { return p.Id !== profile.Id; });
            currentProfileId = null;
            populateProfileDropdown();
            checkDirty();
        });
    }

    function saveConfig() {
        var empty = fullConfig.Profiles.filter(function (p) {
            return !p.IsDefault && TARGETS.every(function (target) {
                return !p[target.key] || p[target.key].length === 0;
            });
        });

        if (empty.length > 0) {
            var names = empty.map(function (p) { return p.Name || 'Unnamed'; }).join(', ');
            shared.setStatus('saveStatus', 'Could not save, since ' + names + ' has nothing assigned. Assign items or remove the profile.', true, { timeout: 0 });
            return;
        }

        if (_saving) return;
        _saving = true;

        // Read-modify-write: designs, logos, and plugin-wide settings live in the same
        // configuration object and are edited on the other tabs.
        shared.getConfig().then(function (serverConfig) {
            serverConfig.Profiles = fullConfig.Profiles;
            return shared.saveConfig(serverConfig);
        }).then(function () {
            tracker.flashSaved();
            shared.setStatus('saveStatus', 'Profiles saved.');
        }).catch(function (error) {
            console.error('Failed to save profiles:', error);
            shared.errorText(error, 'Could not save the profiles.').then(function (text) { shared.setStatus('saveStatus', text, true); });
        }).finally(function () {
            _saving = false;
        });
    }

    function bindEventListeners() {
        view.querySelector('#AgProfilesForm').addEventListener('submit', function (e) {
            e.preventDefault();
            saveConfig();
        });

        view.querySelector('#selectProfile').addEventListener('change', function () {
            currentProfileId = this.value;
            loadCurrentProfile();
        });

        view.querySelector('#btnNewProfile').addEventListener('click', createNewProfile);
        view.querySelector('#btnRenameProfile').addEventListener('click', renameCurrentProfile);
        view.querySelector('#btnDeleteProfile').addEventListener('click', deleteCurrentProfile);

        view.querySelectorAll('[data-backdrop-setting]').forEach(function (el) {
            var evt = el.type === 'checkbox' ? 'change' : 'input';
            el.addEventListener(evt, function () {
                readBackdropSetting(el);
                checkDirty();
            });
        });
    }
}
