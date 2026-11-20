// ag_profiles.js
// The Profiles tab, which picks the images each kind of item gets and the items each profile applies to.

import {
    createArtworkPage,
    createArtworkSaveBar,
    createMatrixGrid,
    createMediaPicker,
    createSubTabs,
    confirmDialog,
    generateGuid,
    initCollapsibles,
    promptName,
    sameId,
    t,
    withLanguage
} from './configurationpage?name=ag_shared.js';

export default function (view) {
    'use strict';

    var fullConfig = null;
    var currentProfileId = null;
    var logoDesigns = [];
    var _slotTabs = null;
    var _grids = [];
    var _assignmentTabs = null;
    var _pickers = [];

    var EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

    var KINDS = [
        { kind: 'Series', label: t.tr('Series'), shapeKey: 'SeriesPrimaryShape', defaultShape: 'Portrait' },
        { kind: 'Season', label: t.tr('HeaderSeasons'), shapeKey: 'SeasonPrimaryShape', defaultShape: 'Portrait' },
        { kind: 'Episode', label: t.tr('Episodes'), shapeKey: 'EpisodePrimaryShape', defaultShape: 'Landscape' },
        { kind: 'Movie', label: t.tr('Movies'), shapeKey: 'MoviePrimaryShape', defaultShape: 'Portrait' },
        { kind: 'Video', label: t.tr('HeaderVideos'), shapeKey: 'VideoPrimaryShape', defaultShape: 'Portrait' },
        { kind: 'Collection', label: t.tr('Collections'), shapeKey: 'CollectionPrimaryShape', defaultShape: 'Portrait' },
        { kind: 'Folder', label: t.tr('Folders'), shapeKey: 'FolderPrimaryShape', defaultShape: 'Landscape' }
    ];

    // The image slots, in the order their tabs show.
    var SLOTS = ['Primary', 'Thumb', 'Logo', 'Backdrop'];

    // Each image's name, as the web client words it.
    var SLOT_LABELS = { Primary: t.tr('Primary'), Thumb: t.tr('Thumb'), Logo: t.tr('Logo'), Backdrop: t.tr('Backdrop') };

    // Mirrors ArtworkProfile.SupportedSlots on the server: Jellyfin clients never show logos for
    // seasons or episodes, so those rows are not offered.
    var SUPPORTED = {
        Series: ['Primary', 'Thumb', 'Logo', 'Backdrop'],
        Season: ['Primary', 'Thumb', 'Backdrop'],
        Episode: ['Primary', 'Thumb', 'Backdrop'],
        Movie: ['Primary', 'Thumb', 'Logo', 'Backdrop'],
        Video: ['Primary', 'Thumb', 'Logo', 'Backdrop'],
        Collection: ['Primary', 'Thumb', 'Logo', 'Backdrop'],
        Folder: ['Primary', 'Thumb', 'Logo', 'Backdrop']
    };

    var page = createArtworkPage(view, 2, {
        bind: function () {
            initCollapsibles(view);
            buildSaveBar();
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

    var tracker = shared.createChangeTracker({ serialize: snapshot });
    page.guardUnsaved(tracker);
    var saveBar = null;

    // buildSaveBar
    // Draws Base's Save Bar under the form.
    function buildSaveBar() {
        saveBar = createArtworkSaveBar(view.querySelector('#AgProfilesForm'), view.querySelector('#saveBarHost'), tracker, {
            save: saveConfig,
            savedText: t.tr('ToastProfilesSaved'),
            failedText: t.tr('ErrorSaveProfiles')
        });
    }

    // checkDirty
    // Shows Unsaved changes while the profiles differ from the last load or save.
    function checkDirty() {
        if (fullConfig) saveBar.check();
    }

    // The backdrop settings' labels and help text live on the settings model, the way the Designs
    // and Logos pages take theirs, so the wording is changed next to the setting itself.
    var backdropText = {};

    function loadBackdropText() {
        return ApiClient.ajax({
            type: 'GET',
            url: withLanguage('Plugins/ArtworkGenerator/SettingOptions'),
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
            saveBar.markClean();
        }).catch(function (error) {
            console.error('Failed to load profiles:', error);
            saveBar.setState('failed', t.tr('ErrorLoadProfiles'));
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
            option.textContent = profile.Name || t.tr('UnnamedProfile');
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

        if (isDefault) {
            destroyAssignments();
        } else {
            renderAssignments();
        }
        renderSlots();
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
            return { value: d.Id, label: d.Name || t.tr('UnnamedDesign') };
        });

        if (!findDesign(currentId) && currentId && !sameId(currentId, EMPTY_GUID)) {
            options.unshift({ value: currentId, label: deletedText || t.tr('OptionDeletedDesign') });
        }

        return options;
    }

    // A secondary or tertiary design is optional, and a deleted one is skipped rather than
    // replaced by the default.
    function extraDesignOptions(currentId) {
        return [{ value: EMPTY_GUID, label: t.tr('None') }].concat(designOptions(currentId, t.tr('OptionDeletedDesignSkipped')));
    }

    function logoOptions(currentId) {
        var options = logoDesigns.map(function (l) { return { value: l.Id, label: l.Name || t.tr('UnnamedLogo') }; });
        var exists = logoDesigns.some(function (l) { return sameId(l.Id, currentId); });
        if (!exists && currentId && !sameId(currentId, EMPTY_GUID)) {
            options.unshift({ value: currentId, label: t.tr('OptionDeletedLogo') });
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

    // The columns each image's grid shows. Generate is a Base toggle and each design a Base select.
    var SLOT_COLUMNS = {
        Primary: ['Enabled', 'Shape', 'DesignId', 'SecondaryDesignId', 'TertiaryDesignId'],
        Thumb: ['Enabled', 'DesignId', 'SecondaryDesignId', 'TertiaryDesignId'],
        Logo: ['Enabled', 'DesignId'],
        Backdrop: ['Enabled']
    };

    var COLUMNS = {
        Enabled: { key: 'Enabled', label: t.tr('LabelGenerate'), icon: 'check' },
        Shape: { key: 'Shape', label: t.tr('LabelShape'), icon: 'crop', type: 'select', options: [{ value: 'Portrait', label: t.tr('OptionPortrait') }, { value: 'Landscape', label: t.tr('OptionLandscape') }] },
        DesignId: { key: 'DesignId', label: t.tr('LabelDesign'), icon: 'brush', type: 'select', options: [] },
        SecondaryDesignId: { key: 'SecondaryDesignId', label: t.tr('LabelSecondDesign'), icon: 'filter_2', type: 'select', options: [] },
        TertiaryDesignId: { key: 'TertiaryDesignId', label: t.tr('LabelThirdDesign'), icon: 'filter_3', type: 'select', options: [] }
    };

    // optionValue
    // Finds the choice that names a stored id, which may differ from it in case or dashes.
    //
    // Inputs
    // options [Array]: The choices, as { value, label }.
    // id [string]: The stored id.
    //
    // Output
    // [string]: The matching choice's value, or the first choice's when none matches.
    function optionValue(options, id) {
        var match = options.find(function (o) { return sameId(o.value, id) || o.value === id; });
        return match ? match.value : (options[0] ? options[0].value : '');
    }

    // slotRows
    // Builds one grid row per kind of item that shows this image. A row whose Generate is off shows only the toggle, and the third design shows once there is a second.
    //
    // Inputs
    // profile [object]: The profile on screen.
    // slot [string]: The image, such as Primary.
    //
    // Output
    // [object]: rows for createMatrixGrid and the value of each cell.
    function slotRows(profile, slot) {
        var rows = [];
        var value = {};

        KINDS.forEach(function (k) {
            if (SUPPORTED[k.kind].indexOf(slot) === -1) return;

            var assignment = getSlot(profile, k.kind, slot);
            var cells = {};
            var cellValue = { Enabled: !!assignment.Enabled };

            // A slot with no design takes the first one, whether or not it is generated.
            if (SLOT_COLUMNS[slot].indexOf('DesignId') !== -1 && sameId(assignment.DesignId, EMPTY_GUID)) {
                var first = slot === 'Logo' ? logoOptions(assignment.DesignId) : designOptions(assignment.DesignId);
                if (first.length > 0) assignment.DesignId = first[0].value;
            }

            // A cell that does not apply yet shows its value grayed out: every choice while Generate is off,
            // and the third design until a second one is picked.
            SLOT_COLUMNS[slot].forEach(function (key) {
                if (key === 'Enabled') return;
                var off = !assignment.Enabled
                    || (key === 'TertiaryDesignId' && sameId(assignment.SecondaryDesignId || EMPTY_GUID, EMPTY_GUID));

                if (key === 'Shape') {
                    if (off) cells.Shape = { disabled: true };
                    cellValue.Shape = profile[k.shapeKey] || k.defaultShape;
                    return;
                }

                var options;
                if (key === 'DesignId') {
                    options = slot === 'Logo' ? logoOptions(assignment.DesignId) : designOptions(assignment.DesignId);
                } else {
                    options = extraDesignOptions(assignment[key] || EMPTY_GUID);
                }

                cells[key] = off ? { options: options, disabled: true } : { options: options };
                cellValue[key] = optionValue(options, assignment[key] || EMPTY_GUID);
            });

            rows.push({ key: k.kind, label: k.label, cells: cells });
            value[k.kind] = cellValue;
        });

        return { rows: rows, value: value };
    }

    // unsupportedNote
    // Names the kinds of item Jellyfin clients never show this image for, listed the way the viewer's language lists them.
    //
    // Inputs
    // slot [string]: The image, such as Logo.
    //
    // Output
    // [string]: The note, or an empty string when every kind shows it.
    function unsupportedNote(slot) {
        var missing = KINDS.filter(function (k) { return SUPPORTED[k.kind].indexOf(slot) === -1; })
            .map(function (k) { return k.label; });
        if (!missing.length) return '';
        return t.format('NoteImageNotShown', new Intl.ListFormat(t.language, { type: 'disjunction' }).format(missing));
    }

    // buildSlotGrid
    // Draws one image's Base Matrix Grid, with a row per kind of item and a column per choice.
    //
    // Inputs
    // profile [object]: The profile on screen.
    // slot [string]: The image, such as Primary.
    //
    // Output
    // [object]: The grid handle.
    function buildSlotGrid(profile, slot) {
        var built = slotRows(profile, slot);
        var grid = createMatrixGrid({
            rows: built.rows,
            columns: SLOT_COLUMNS[slot].map(function (key) { return COLUMNS[key]; }),
            value: built.value,
            rowHeader: t.tr('LabelItem'),
            label: t.format('LabelSlotImages', SLOT_LABELS[slot]),
            note: unsupportedNote(slot),
            onChange: function (value, kind, key) {
                var assignment = getSlot(profile, kind, slot);
                var cell = value[kind][key];
                var redraw = false;

                if (key === 'Enabled') {
                    assignment.Enabled = !!cell;
                    redraw = true;
                } else if (key === 'Shape') {
                    var k = KINDS.find(function (entry) { return entry.kind === kind; });
                    profile[k.shapeKey] = cell;
                } else {
                    assignment[key] = cell;
                }

                // Clearing the second design moves the third up into its place, so the two never leave a gap.
                if (key === 'SecondaryDesignId' || key === 'TertiaryDesignId') {
                    if (sameId(assignment.SecondaryDesignId || EMPTY_GUID, EMPTY_GUID) && !sameId(assignment.TertiaryDesignId || EMPTY_GUID, EMPTY_GUID)) {
                        assignment.SecondaryDesignId = assignment.TertiaryDesignId;
                        assignment.TertiaryDesignId = EMPTY_GUID;
                    }
                    redraw = true;
                }

                if (redraw) {
                    var next = slotRows(profile, slot);
                    grid.setRows(next.rows);
                    grid.setValue(next.value);
                }

                checkDirty();
            }
        });
        return grid;
    }

    // renderSlots
    // Draws a Base Sub Tab for each image, each holding that image's grid, and keeps the tab the reader had open.
    function renderSlots() {
        var profile = getCurrentProfile();
        var host = view.querySelector('#slotTabs');
        var selected = _slotTabs ? _slotTabs.getValue() : SLOTS[0];
        destroySlots();
        if (!profile) return;

        // Every image's assignments are filled in now, not when its tab first opens, so opening a tab never reads as an edit.
        SLOTS.forEach(function (slot) { slotRows(profile, slot); });

        _slotTabs = createSubTabs({
            label: t.tr('Images'),
            value: selected,
            tabs: SLOTS.map(function (slot) { return { id: slot, label: SLOT_LABELS[slot] }; }),
            render: function (tab) {
                var grid = buildSlotGrid(profile, tab.id);
                _grids.push(grid);
                return grid.element;
            }
        });
        host.appendChild(_slotTabs.element);
    }

    // destroySlots
    // Removes the image tabs and their grids.
    function destroySlots() {
        _grids.forEach(function (grid) { grid.destroy(); });
        _grids = [];
        if (_slotTabs) _slotTabs.destroy();
        _slotTabs = null;
        view.querySelector('#slotTabs').innerHTML = '';
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
            tab: t.tr('Series'),
            label: t.tr('LabelAssignedSeries'),
            search: t.tr('PlaceholderSearchSeries'),
            empty: t.tr('EmptyAssignedSeries'),
            missing: t.tr('EmptyFoundSeries'),
            noMatch: t.tr('EmptyMatchSeries'),
            items: []
        },
        {
            key: 'MovieIds',
            itemType: 'Movie',
            noun: 'movies',
            tab: t.tr('Movies'),
            label: t.tr('LabelAssignedMovies'),
            search: t.tr('PlaceholderSearchMovies'),
            empty: t.tr('EmptyAssignedMovies'),
            missing: t.tr('EmptyFoundMovies'),
            noMatch: t.tr('EmptyMatchMovies'),
            items: []
        },
        {
            key: 'VideoIds',

            // Music videos and home videos are both plain videos to Jellyfin, so one query covers
            // everything a library holds that is not part of a series and not a film.
            itemType: 'MusicVideo,Video',
            noun: 'videos',
            tab: t.tr('HeaderVideos'),
            label: t.tr('LabelAssignedVideos'),
            search: t.tr('PlaceholderSearchVideos'),
            empty: t.tr('EmptyAssignedVideos'),
            missing: t.tr('EmptyFoundVideos'),
            noMatch: t.tr('EmptyMatchVideos'),
            items: []
        },
        {
            key: 'CollectionIds',
            itemType: 'BoxSet',
            noun: 'collections',
            tab: t.tr('Collections'),
            label: t.tr('LabelAssignedCollections'),
            search: t.tr('PlaceholderSearchCollections'),
            empty: t.tr('EmptyAssignedCollections'),
            missing: t.tr('EmptyFoundCollections'),
            noMatch: t.tr('EmptyMatchCollections'),
            items: []
        },
        {
            key: 'FolderIds',
            itemType: 'Folder',
            noun: 'folders',
            tab: t.tr('Folders'),
            label: t.tr('LabelAssignedFolders'),
            search: t.tr('PlaceholderSearchFolders'),
            empty: t.tr('EmptyAssignedFolders'),
            missing: t.tr('EmptyFoundFolders'),
            noMatch: t.tr('EmptyMatchFolders'),
            items: []
        }
    ];

    function loadAssignableItems() {
        return Promise.all(TARGETS.map(function (target) {
            var request = ApiClient.getItems(ApiClient.getCurrentUserId(), {
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

    // Item types named on the second line of a picked item.
    var TYPE_LABELS = { BoxSet: t.tr('LabelCollection'), MusicVideo: t.tr('MusicVideo') };

    // destroyAssignments
    // Removes the assignment tabs and their pickers.
    function destroyAssignments() {
        _pickers.forEach(function (picker) { picker.destroy(); });
        _pickers = [];
        if (_assignmentTabs) _assignmentTabs.destroy();
        _assignmentTabs = null;
        view.querySelector('#assignmentTabs').innerHTML = '';
    }

    // renderAssignments
    // Draws a Base Sub Tab for each kind of item a profile can be assigned, each holding a Media Picker, and keeps the tab the reader had open.
    function renderAssignments() {
        var profile = getCurrentProfile();
        var selected = _assignmentTabs ? _assignmentTabs.getValue() : TARGETS[0].key;
        destroyAssignments();
        if (!profile) return;

        _assignmentTabs = createSubTabs({
            label: t.tr('HeaderAssignedItems'),
            value: selected,
            tabs: TARGETS.map(function (target) { return { id: target.key, label: target.tab }; }),
            render: function (tab) {
                var target = TARGETS.find(function (entry) { return entry.key === tab.id; });
                var picker = buildPicker(profile, target);
                _pickers.push(picker);
                return picker.element;
            }
        });
        view.querySelector('#assignmentTabs').appendChild(_assignmentTabs.element);
    }

    // findItem
    // Finds a loaded item by its stored id.
    //
    // Inputs
    // target [object]: The assignable kind.
    // id [string]: The stored id.
    //
    // Output
    // [object|undefined]: The item.
    function findItem(target, id) {
        return target.items.find(function (candidate) { return sameId(candidate.Id, id); });
    }

    // buildPicker
    // Draws a Base Media Picker over the loaded items of one kind. Items another profile holds are left out of the results.
    //
    // Inputs
    // profile [object]: The profile on screen.
    // target [object]: The assignable kind.
    //
    // Output
    // [object]: The picker handle.
    function buildPicker(profile, target) {
        var picked = assignedIds(profile, target).map(function (id) { return findItem(target, id); }).filter(Boolean);

        return createMediaPicker({
            label: target.label,
            value: picked,
            placeholder: target.search,
            emptyText: target.empty,
            noResultsText: target.items.length ? target.noMatch : target.missing,
            typeLabels: TYPE_LABELS,
            minChars: 1,
            searchFn: function (query) {
                var wanted = query.toLowerCase();
                var elsewhere = getAssignedElsewhere(target);
                return Promise.resolve(target.items.filter(function (item) {
                    return String(item.Name || '').toLowerCase().indexOf(wanted) !== -1
                        && !elsewhere.some(function (other) { return sameId(other, item.Id); });
                }));
            },
            onChange: function (items) {
                // An id whose item is not in the library today stays assigned, as it was stored.
                var unknown = assignedIds(profile, target).filter(function (id) { return !findItem(target, id); });
                profile[target.key] = items.map(function (item) { return item.Id; }).concat(unknown);
                checkDirty();
            }
        });
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
        if (!name) return t.tr('ErrorEnterName');
        if (name.toLowerCase() === 'default') return t.tr('ErrorDefaultNameReserved');
        if (nameIsTaken(name, exceptId)) return t.tr('ErrorProfileNameTaken');
        return null;
    }

    function createNewProfile() {
        promptName(t.tr('HeaderNewProfile'), '', t.tr('Anime'), function (name) { return checkName(name); }).then(function (name) {
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

        promptName(t.tr('HeaderRenameProfile'), profile.Name || '', '', function (name) { return checkName(name, profile.Id); }).then(function (name) {
            if (!name) return;

            profile.Name = name;
            populateProfileDropdown();
            checkDirty();
        });
    }

    function deleteCurrentProfile() {
        var profile = getCurrentProfile();
        if (profile.IsDefault) return;

        confirmDialog(t.format('ConfirmRemoveProfile', profile.Name || t.tr('UnnamedProfile')), {
            title: t.tr('HeaderRemoveProfile'),
            confirmText: t.tr('ButtonRemove'),
            destructive: true
        }).then(function (confirmed) {
            if (!confirmed) return;
            fullConfig.Profiles = fullConfig.Profiles.filter(function (p) { return p.Id !== profile.Id; });
            currentProfileId = null;
            populateProfileDropdown();
            checkDirty();
        });
    }

    // saveConfig
    // Saves the profiles, once every profile but the default has something assigned.
    //
    // Output
    // [Promise]: Resolves once saved, or rejects with the reason.
    function saveConfig() {
        var empty = fullConfig.Profiles.filter(function (p) {
            return !p.IsDefault && TARGETS.every(function (target) {
                return !p[target.key] || p[target.key].length === 0;
            });
        });

        if (empty.length > 0) {
            var names = empty.map(function (p) { return p.Name || t.tr('Unnamed'); }).join(', ');
            return Promise.reject(t.format('ErrorProfileUnassigned', names));
        }

        // Read-modify-write: designs, logos, and plugin-wide settings live in the same
        // configuration object and are edited on the other tabs.
        return shared.getConfig().then(function (serverConfig) {
            serverConfig.Profiles = fullConfig.Profiles;
            return shared.saveConfig(serverConfig);
        }).catch(function (error) {
            console.error('Failed to save profiles:', error);
            throw error;
        });
    }

    function bindEventListeners() {
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

    view.addEventListener('viewdestroy', function () {
        destroyAssignments();
        destroySlots();
    });
}
