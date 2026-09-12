import { initCollapsibles, setTabs, createShared } from '/web/configurationpage?name=ag_jpkribs_shared.js';

export default function (view) {
    'use strict';

    var pluginId = 'b8715e44-6b77-4c88-9c74-2b6f4c7b9a1e';
    var shared = createShared(view, pluginId, 'Plugins/ArtworkGenerator');
    var fullConfig = null;
    var currentProfileId = null;
    var logoDesigns = [];
    var _initialized = false;
    var _dirty = false;
    var _saving = false;
    var _snapshot = null;
    var _modalTrigger = null;
    var _modalTarget = null;

    var EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

    var KINDS = [
        { kind: 'Series', label: 'Series', shapeKey: 'SeriesPrimaryShape', defaultShape: 'Portrait' },
        { kind: 'Season', label: 'Seasons', shapeKey: 'SeasonPrimaryShape', defaultShape: 'Portrait' },
        { kind: 'Episode', label: 'Episodes', shapeKey: 'EpisodePrimaryShape', defaultShape: 'Landscape' },
        { kind: 'Movie', label: 'Movies', shapeKey: 'MoviePrimaryShape', defaultShape: 'Portrait' }
    ];

    // Which kinds each scope draws, mirroring ArtworkProfile.AppliesTo on the server.
    var SLOTS = ['Primary', 'Thumb', 'Logo', 'Backdrop'];

    // Mirrors ArtworkProfile.SupportedSlots on the server: Jellyfin clients never show logos for
    // seasons or episodes, so those cells are not offered.
    var SUPPORTED = {
        Series: ['Primary', 'Thumb', 'Logo', 'Backdrop'],
        Season: ['Primary', 'Thumb', 'Backdrop'],
        Episode: ['Primary', 'Thumb', 'Backdrop'],
        Movie: ['Primary', 'Thumb', 'Logo', 'Backdrop']
    };

    function getTabs() {
        return [
            { href: 'configurationpage?name=ag_posters', name: 'Designs' },
            { href: 'configurationpage?name=ag_logos', name: 'Logos' },
            { href: 'configurationpage?name=ag_profiles', name: 'Profiles' },
            { href: 'configurationpage?name=ag_settings', name: 'Settings' }
        ];
    }

    // ── Utilities ────────────────────────────────────────────

    function generateGuid() {
        return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, function (c) {
            var r = Math.random() * 16 | 0, v = c === 'x' ? r : (r & 0x3 | 0x8);
            return v.toString(16);
        });
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

    function sameId(a, b) {
        return String(a || '').replace(/-/g, '').toLowerCase() === String(b || '').replace(/-/g, '').toLowerCase();
    }

    // ── Unsaved Changes ─────────────────────────────────────

    function snapshot() {
        return JSON.stringify(fullConfig ? fullConfig.Profiles : null);
    }

    function takeSnapshot() {
        _snapshot = snapshot();
    }

    function setDirty(dirty) {
        _dirty = dirty;
        var indicator = view.querySelector('#unsavedIndicator');
        if (indicator) indicator.classList.toggle('visible', dirty);
    }

    function checkDirty() {
        if (!fullConfig || _snapshot === null) return;
        setDirty(snapshot() !== _snapshot);
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
            input.type = 'text';
            input.id = 'inputModalField_' + index;
            input.value = field.value || '';
            if (field.placeholder) input.placeholder = field.placeholder;
            container.appendChild(input);

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
            fieldsContainer.querySelectorAll('input').forEach(function (input) { values.push(input.value); });
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

    // ── Loading ─────────────────────────────────────────────

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
        Dashboard.showLoadingMsg();
        Promise.all([shared.getConfig(), loadAssignableItems(), fetchLogos()]).then(function (results) {
            fullConfig = results[0];
            fullConfig.Profiles = fullConfig.Profiles || [];
            fullConfig.PosterConfigurations = fullConfig.PosterConfigurations || [];
            logoDesigns = results[2] || [];

            // The server always supplies a default profile; this only guards a malformed payload.
            if (!fullConfig.Profiles.some(function (p) { return p.IsDefault; })) {
                if (fullConfig.Profiles.length > 0) {
                    fullConfig.Profiles[0].IsDefault = true;
                } else {
                    fullConfig.Profiles.push({ Id: generateGuid(), Name: 'Default', IsDefault: true, SeriesIds: [], Slots: [], Backdrop: {} });
                }
            }

            populateProfileDropdown();
            takeSnapshot();
            setDirty(false);
            Dashboard.hideLoadingMsg();
        }).catch(function (error) {
            console.error('Failed to load profiles:', error);
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Failed to load profiles. Please reload the page.', 'Error');
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
        profile.SeriesIds = profile.SeriesIds || [];
        profile.MovieIds = profile.MovieIds || [];
        profile.Backdrop = profile.Backdrop || {};

        var isDefault = !!profile.IsDefault;
        view.querySelector('#btnDeleteProfile').classList.toggle('hidden', isDefault);
        view.querySelector('#btnRenameProfile').classList.toggle('hidden', isDefault);
        view.querySelector('#assignmentSection').style.display = isDefault ? 'none' : 'block';

        if (!isDefault) renderAssignments();
        renderMatrix();
        loadBackdropSettings();
    }

    // ── Designs ─────────────────────────────────────────────

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
    function designOptions(currentId) {
        var options = sortedDesigns().map(function (d) {
            return { value: d.Id, text: d.Name || 'Unnamed Design' };
        });

        if (!findDesign(currentId) && currentId && !sameId(currentId, EMPTY_GUID)) {
            options.unshift({ value: currentId, text: 'Deleted design (uses the default)' });
        }

        return options;
    }

    function logoOptions(currentId) {
        var options = logoDesigns.map(function (l) { return { value: l.Id, text: l.Name || 'Unnamed Logo' }; });
        var exists = logoDesigns.some(function (l) { return sameId(l.Id, currentId); });
        if (!exists && currentId && !sameId(currentId, EMPTY_GUID)) {
            options.unshift({ value: currentId, text: 'Deleted logo (uses the default)' });
        }
        return options;
    }

    // ── Slot Matrix ─────────────────────────────────────────

    function getSlot(profile, kind, slot) {
        var assignment = profile.Slots.find(function (s) { return s.Kind === kind && s.Slot === slot; });
        if (!assignment) {
            assignment = { Kind: kind, Slot: slot, Enabled: false, DesignId: EMPTY_GUID };
            profile.Slots.push(assignment);
        }
        return assignment;
    }

    function makeSelect(options, value, label) {
        var select = document.createElement('select');
        select.className = 'emby-select slot-select';
        if (label) select.setAttribute('aria-label', label);

        options.forEach(function (o) {
            var option = document.createElement('option');
            option.value = o.value;
            option.textContent = o.text;
            select.appendChild(option);
        });

        var match = options.find(function (o) { return sameId(o.value, value) || o.value === value; });
        if (match) select.value = match.value;
        return select;
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
                    td.className = 'slot-cell-unavailable';
                    td.textContent = 'Not used';
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
        cell.className = 'slot-cell';

        var toggle = document.createElement('label');
        toggle.className = 'slot-toggle';
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
        } else if (slot === 'Backdrop') {
            var note = document.createElement('div');
            note.className = 'fieldDescription';
            note.textContent = 'A frame from the video, no design.';
            cell.appendChild(note);
        }

        function updateState() {
            controls.forEach(function (control) { control.disabled = !assignment.Enabled; });
            cell.classList.toggle('slot-cell-off', !assignment.Enabled);
        }

        checkbox.addEventListener('change', function () {
            assignment.Enabled = checkbox.checked;
            updateState();
            checkDirty();
        });

        updateState();
        return cell;
    }

    // ── Backdrop Settings ───────────────────────────────────

    var BACKDROP_DEFAULTS = {
        AspectRatio: '16:9',
        EnableLetterboxDetection: true,
        LetterboxBlackThreshold: 25,
        LetterboxConfidence: 85,
        BrightenHDR: 25,
        ExtractWindowStart: 20,
        ExtractWindowEnd: 80
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
        updateBackdropVisibility();
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

    function updateBackdropVisibility() {
        var letterbox = view.querySelector('#chkBackdropLetterbox');
        view.querySelector('#backdropLetterboxOptions').style.display = letterbox && letterbox.checked ? 'block' : 'none';
    }

    // ── Assignment ──────────────────────────────────────────

    // A profile draws every kind of item, so what it applies to is decided purely by assignment.
    // Series and films differ only in which list they are stored in and what they are called, so
    // one set of functions serves both rather than two near-identical copies.
    var TARGETS = [
        {
            key: 'SeriesIds',
            itemType: 'Series',
            noun: 'series',
            label: 'Assigned Series:',
            button: 'Edit Series',
            modalTitle: 'Select Series',
            search: 'Search series...',
            empty: 'No series assigned.',
            missing: 'No series found. Make sure you have TV series in your Jellyfin library.',
            items: []
        },
        {
            key: 'MovieIds',
            itemType: 'Movie',
            noun: 'movies',
            label: 'Assigned Movies:',
            button: 'Edit Movies',
            modalTitle: 'Select Movies',
            search: 'Search movies...',
            empty: 'No movies assigned.',
            missing: 'No movies found. Make sure you have movies in your Jellyfin library.',
            items: []
        }
    ];

    function loadAssignableItems() {
        return Promise.all(TARGETS.map(function (target) {
            return ApiClient.getItems(ApiClient.getCurrentUserId(), {
                IncludeItemTypes: target.itemType,
                Recursive: true,
                SortBy: 'SortName',
                SortOrder: 'Ascending',
                Fields: 'Overview,ProductionYear'
            }).then(function (result) {
                target.items = result.Items || [];
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
        list.className = 'assigned-list';
        block.appendChild(list);

        var ids = assignedIds(profile, target);
        if (ids.length === 0) {
            var msg = document.createElement('div');
            msg.className = 'assigned-empty';
            var icon = document.createElement('span');
            icon.className = 'assigned-empty-icon';
            icon.innerHTML = '&#9888;';
            msg.appendChild(icon);
            msg.appendChild(document.createTextNode(' ' + target.empty));
            list.appendChild(msg);
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
        button.className = 'raised';
        button.style.marginTop = '8px';
        button.appendChild(document.createElement('span')).textContent = target.button;
        button.addEventListener('click', function () { showSelectionModal(target); });
        block.appendChild(button);

        return block;
    }

    function buildAssignedTag(item, target, id) {
        var tag = document.createElement('div');
        tag.className = 'assigned-tag';

        var img = document.createElement('img');
        img.className = 'assigned-tag-poster';
        img.src = ApiClient.getImageUrl(item.Id, { type: 'Primary', maxWidth: 64, quality: 90 });
        img.onerror = function () { this.style.display = 'none'; };

        var name = document.createElement('span');
        name.className = 'assigned-tag-name';
        name.textContent = item.Name;

        var remove = document.createElement('span');
        remove.className = 'assigned-tag-remove';
        remove.textContent = '\u00d7';
        remove.addEventListener('click', function () { removeAssignment(target, id); });

        tag.appendChild(img);
        tag.appendChild(name);
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

    function showSelectionModal(target) {
        if (!target.items || target.items.length === 0) {
            Dashboard.alert(target.missing);
            return;
        }

        _modalTrigger = document.activeElement;
        _modalTarget = target;

        view.querySelector('#pickerTitle').textContent = target.modalTitle;
        view.querySelector('#pickerSearch').placeholder = target.search;
        view.querySelector('#pickerModal').style.display = 'flex';

        document.addEventListener('keydown', onSelectionModalKeydown);
        populateSelectionModal();
    }

    function populateSelectionModal() {
        var target = _modalTarget;
        var listContainer = view.querySelector('#pickerList');
        var summaryEl = view.querySelector('#pickerSummary');
        var current = assignedIds(getCurrentProfile(), target);
        var elsewhere = getAssignedElsewhere(target);
        var availableCount = 0;

        listContainer.innerHTML = '';

        target.items.forEach(function (entry) {
            var isHere = current.some(function (id) { return sameId(id, entry.Id); });
            var isElsewhere = !isHere && elsewhere.some(function (id) { return sameId(id, entry.Id); });
            if (!isElsewhere) availableCount++;

            var item = document.createElement('label');
            item.className = 'picker-row' + (isElsewhere ? ' disabled' : '');

            var checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.className = 'picker-check';
            checkbox.value = entry.Id;
            checkbox.checked = isHere;
            checkbox.disabled = isElsewhere;

            var poster = document.createElement('img');
            poster.className = 'picker-item-poster';
            poster.src = ApiClient.getImageUrl(entry.Id, { type: 'Primary', maxWidth: 80, quality: 80 });
            poster.onerror = function () { this.style.visibility = 'hidden'; };

            var info = document.createElement('div');
            info.className = 'picker-item-info';

            var nameSpan = document.createElement('span');
            nameSpan.className = 'picker-item-name';
            nameSpan.textContent = entry.Name;
            info.appendChild(nameSpan);

            if (entry.ProductionYear) {
                var yearSpan = document.createElement('span');
                yearSpan.className = 'picker-item-year';
                yearSpan.textContent = entry.ProductionYear;
                info.appendChild(yearSpan);
            }

            if (isElsewhere) {
                var badge = document.createElement('span');
                badge.className = 'picker-item-badge';
                badge.textContent = 'In another profile';
                info.appendChild(badge);
            }

            item.appendChild(checkbox);
            item.appendChild(poster);
            item.appendChild(info);
            listContainer.appendChild(item);

            checkbox.addEventListener('change', updateSummary);
        });

        function updateSummary() {
            var checked = listContainer.querySelectorAll('.picker-check:checked').length;
            summaryEl.textContent = checked + ' of ' + availableCount + ' available ' + target.noun + ' selected';
        }

        updateSummary();

        var searchInput = view.querySelector('#pickerSearch');
        searchInput.value = '';
        searchInput.focus();
    }

    var filterSelectionList = debounce(function () {
        var term = view.querySelector('#pickerSearch').value.toLowerCase();
        view.querySelectorAll('.picker-row').forEach(function (item) {
            var name = item.querySelector('.picker-item-name');
            item.style.display = (name ? name.textContent : item.textContent).toLowerCase().includes(term) ? '' : 'none';
        });
    }, 200);

    function confirmSelection() {
        var selected = [];
        view.querySelectorAll('.picker-check:checked').forEach(function (cb) { selected.push(cb.value); });
        getCurrentProfile()[_modalTarget.key] = selected;
        renderAssignments();
        closeSelectionModal();
        checkDirty();
    }

    function onSelectionModalKeydown(e) {
        if (e.key === 'Escape') {
            e.preventDefault();
            closeSelectionModal();
        } else {
            trapFocus(view.querySelector('.picker-content'), e);
        }
    }

    function closeSelectionModal() {
        view.querySelector('#pickerModal').style.display = 'none';
        document.removeEventListener('keydown', onSelectionModalKeydown);
        if (_modalTrigger && _modalTrigger.focus) _modalTrigger.focus();
        _modalTrigger = null;
        _modalTarget = null;
    }

    function removeAssignment(target, id) {
        var profile = getCurrentProfile();
        profile[target.key] = assignedIds(profile, target).filter(function (existing) { return !sameId(existing, id); });
        renderAssignments();
        checkDirty();
    }

    // ── Profile CRUD ────────────────────────────────────────

    function nameIsTaken(name, exceptId) {
        return fullConfig.Profiles.some(function (p) {
            return p.Id !== exceptId && p.Name && p.Name.toLowerCase() === name.toLowerCase();
        });
    }

    function createNewProfile() {
        showInputModal('New Profile', [{ label: 'Name', placeholder: 'Anime' }], function (values) {
            if (!values) return;
            var name = (values[0] || '').trim();

            if (!name) { Dashboard.alert('Profile name is required.'); return; }
            if (name.toLowerCase() === 'default') { Dashboard.alert('The name "Default" is reserved.'); return; }
            if (nameIsTaken(name)) { Dashboard.alert('A profile with this name already exists.'); return; }

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

        showInputModal('Rename Profile', [{ label: 'Name', value: profile.Name || '' }], function (values) {
            if (!values) return;
            var name = (values[0] || '').trim();

            if (!name) return;
            if (name.toLowerCase() === 'default') { Dashboard.alert('The name "Default" is reserved.'); return; }
            if (nameIsTaken(name, profile.Id)) { Dashboard.alert('A profile with this name already exists.'); return; }

            profile.Name = name;
            populateProfileDropdown();
            checkDirty();
        });
    }

    function deleteCurrentProfile() {
        var profile = getCurrentProfile();
        if (profile.IsDefault) return;

        Dashboard.confirm('Delete this profile? Its series will use the default profile.', 'Delete Profile', function (confirmed) {
            if (!confirmed) return;
            fullConfig.Profiles = fullConfig.Profiles.filter(function (p) { return p.Id !== profile.Id; });
            currentProfileId = null;
            populateProfileDropdown();
            checkDirty();
        });
    }

    // ── Save ────────────────────────────────────────────────

    function saveConfig() {
        var empty = fullConfig.Profiles.filter(function (p) {
            // A profile is assigned by series or by film; either one is enough.
            return !p.IsDefault
                && (!p.SeriesIds || p.SeriesIds.length === 0)
                && (!p.MovieIds || p.MovieIds.length === 0);
        });

        if (empty.length > 0) {
            var names = empty.map(function (p) { return p.Name || 'Unnamed'; }).join(', ');
            Dashboard.alert('Cannot save. These profiles have no series assigned: ' + names + '. Assign series or delete them.');
            return;
        }

        if (_saving) return;
        _saving = true;
        Dashboard.showLoadingMsg();

        // Read-modify-write: designs, logos, and plugin-wide settings live in the same
        // configuration object and are edited on the other tabs.
        shared.getConfig().then(function (serverConfig) {
            serverConfig.Profiles = fullConfig.Profiles;
            return shared.saveConfig(serverConfig);
        }).then(function (result) {
            takeSnapshot();
            setDirty(false);
            flashSaveSuccess();
            Dashboard.processPluginConfigurationUpdateResult(result);
        }).catch(function (error) {
            console.error('Failed to save profiles:', error);
            Dashboard.hideLoadingMsg();
            Dashboard.alert('Failed to save. Please check the server log and try again.', 'Error');
        }).finally(function () {
            _saving = false;
        });
    }

    // ── Event Binding ───────────────────────────────────────

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

        view.querySelector('#btnCancelPicker').addEventListener('click', closeSelectionModal);
        view.querySelector('#btnClosePicker').addEventListener('click', closeSelectionModal);
        view.querySelector('#btnConfirmPicker').addEventListener('click', confirmSelection);
        view.querySelector('#pickerModal').addEventListener('click', function (e) {
            if (e.target === this) closeSelectionModal();
        });
        view.querySelector('#pickerSearch').addEventListener('input', filterSelectionList);

        view.querySelectorAll('[data-backdrop-setting]').forEach(function (el) {
            var evt = el.type === 'checkbox' ? 'change' : 'input';
            el.addEventListener(evt, function () {
                readBackdropSetting(el);
                updateBackdropVisibility();
                checkDirty();
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
        setTabs('ag', 2, getTabs());

        if (!_initialized) {
            _initialized = true;
            initCollapsibles(view);
            bindEventListeners();
        }

        window.addEventListener('beforeunload', onBeforeUnload);
        loadConfig();
    });

    view.addEventListener('viewbeforehide', function (e) {
        window.removeEventListener('beforeunload', onBeforeUnload);

        if (_dirty) {
            var confirmed = confirm('You have unsaved changes. Are you sure you want to leave?');
            if (!confirmed) {
                e.preventDefault();
                setTabs('ag', 2, getTabs());
            }
        }
    });
}
