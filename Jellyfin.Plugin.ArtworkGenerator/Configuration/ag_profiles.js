import { initCollapsibles, setTabs, createShared } from '/web/configurationpage?name=ag_jpkribs_shared.js';

export default function (view) {
    'use strict';

    var pluginId = 'b8715e44-6b77-4c88-9c74-2b6f4c7b9a1e';
    var shared = createShared(view, pluginId, 'Plugins/ArtworkGenerator');
    var fullConfig = null;
    var currentProfileId = null;
    var allSeries = [];
    var logoDesigns = [];
    var _initialized = false;
    var _dirty = false;
    var _saving = false;
    var _snapshot = null;
    var _seriesModalTrigger = null;

    var EMPTY_GUID = '00000000-0000-0000-0000-000000000000';

    var KINDS = [
        { kind: 'Series', label: 'Series', shapeKey: 'SeriesPrimaryShape', defaultShape: 'Portrait' },
        { kind: 'Season', label: 'Seasons', shapeKey: 'SeasonPrimaryShape', defaultShape: 'Portrait' },
        { kind: 'Episode', label: 'Episodes', shapeKey: 'EpisodePrimaryShape', defaultShape: 'Landscape' },
        { kind: 'Movie', label: 'Movies', shapeKey: 'MoviePrimaryShape', defaultShape: 'Portrait' }
    ];

    var SCOPES = [
        { value: 'Tv', label: 'TV' },
        { value: 'Movies', label: 'Movies' },
        { value: 'Both', label: 'TV and Movies' }
    ];

    // Which kinds each scope draws, mirroring ArtworkProfile.AppliesTo on the server.
    var SCOPE_KINDS = {
        Tv: ['Series', 'Season', 'Episode'],
        Movies: ['Movie'],
        Both: ['Series', 'Season', 'Episode', 'Movie']
    };

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
        Promise.all([shared.getConfig(), loadAllSeries(), fetchLogos()]).then(function (results) {
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

    function populateScopeOptions() {
        var select = view.querySelector('#selectProfileScope');
        if (!select || select.options.length) return;

        SCOPES.forEach(function (scope) {
            var option = document.createElement('option');
            option.value = scope.value;
            option.textContent = scope.label;
            select.appendChild(option);
        });
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

        // A profile saved before films existed carries no scope, and was a TV profile.
        profile.Scope = profile.Scope || 'Tv';
        populateScopeOptions();
        view.querySelector('#selectProfileScope').value = profile.Scope;

        var isDefault = !!profile.IsDefault;
        view.querySelector('#btnDeleteProfile').classList.toggle('hidden', isDefault);
        view.querySelector('#btnRenameProfile').classList.toggle('hidden', isDefault);
        // Series are picked here; a films profile is assigned by film, which this list cannot do.
        var picksSeries = profile.Scope !== 'Movies';
        view.querySelector('#seriesAssignmentSection').style.display = (isDefault || !picksSeries) ? 'none' : 'block';

        if (!isDefault && picksSeries) renderAssignedSeries();
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
        var covered = SCOPE_KINDS[profile.Scope] || SCOPE_KINDS.Tv;
        KINDS.filter(function (k) { return covered.indexOf(k.kind) !== -1; }).forEach(function (k) {
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

    // ── Series Assignment ───────────────────────────────────

    function loadAllSeries() {
        return ApiClient.getItems(ApiClient.getCurrentUserId(), {
            IncludeItemTypes: 'Series',
            Recursive: true,
            SortBy: 'SortName',
            SortOrder: 'Ascending',
            Fields: 'Overview,ProductionYear'
        }).then(function (result) {
            allSeries = result.Items || [];
            return allSeries;
        }).catch(function (error) {
            console.error('Failed to load series:', error);
            allSeries = [];
            return [];
        });
    }

    function renderAssignedSeries() {
        var profile = getCurrentProfile();
        var container = view.querySelector('#assignedSeriesList');
        container.innerHTML = '';

        if (!profile.SeriesIds || profile.SeriesIds.length === 0) {
            var msg = document.createElement('div');
            msg.className = 'series-empty-state';
            msg.innerHTML = '<span class="series-empty-state-icon">&#9888;</span> No series assigned. Assign at least one series before saving.';
            container.appendChild(msg);
            return;
        }

        profile.SeriesIds.forEach(function (seriesId) {
            var series = allSeries.find(function (s) { return sameId(s.Id, seriesId); });
            if (!series) return;

            var tag = document.createElement('div');
            tag.className = 'series-tag';

            var img = document.createElement('img');
            img.className = 'series-tag-poster';
            img.src = ApiClient.getImageUrl(series.Id, { type: 'Primary', maxWidth: 64, quality: 90 });
            img.onerror = function () { this.style.display = 'none'; };

            var name = document.createElement('span');
            name.className = 'series-tag-name';
            name.textContent = series.Name;

            var remove = document.createElement('span');
            remove.className = 'series-tag-remove';
            remove.textContent = '×';
            remove.addEventListener('click', function () { removeSeries(seriesId); });

            tag.appendChild(img);
            tag.appendChild(name);
            tag.appendChild(remove);
            container.appendChild(tag);
        });
    }

    function getSeriesAssignedElsewhere() {
        var ids = [];
        fullConfig.Profiles.forEach(function (p) {
            if (p.Id !== currentProfileId && !p.IsDefault && p.SeriesIds) ids.push.apply(ids, p.SeriesIds);
        });
        return ids;
    }

    function showSeriesSelectionModal() {
        _seriesModalTrigger = document.activeElement;
        var modal = view.querySelector('#seriesSelectionModal');

        if (!allSeries || allSeries.length === 0) {
            Dashboard.alert('No series found. Make sure you have TV series in your Jellyfin library.');
            return;
        }

        modal.style.display = 'flex';
        document.addEventListener('keydown', onSeriesModalKeydown);
        populateSeriesModal();
    }

    function populateSeriesModal() {
        var listContainer = view.querySelector('#seriesCheckboxList');
        var summaryEl = view.querySelector('#seriesSelectionSummary');
        var current = getCurrentProfile().SeriesIds || [];
        var elsewhere = getSeriesAssignedElsewhere();
        var availableCount = 0;

        listContainer.innerHTML = '';

        allSeries.forEach(function (series) {
            var isHere = current.some(function (id) { return sameId(id, series.Id); });
            var isElsewhere = !isHere && elsewhere.some(function (id) { return sameId(id, series.Id); });
            if (!isElsewhere) availableCount++;

            var item = document.createElement('label');
            item.className = 'series-checkbox-item' + (isElsewhere ? ' disabled' : '');

            var checkbox = document.createElement('input');
            checkbox.type = 'checkbox';
            checkbox.className = 'series-checkbox';
            checkbox.value = series.Id;
            checkbox.checked = isHere;
            checkbox.disabled = isElsewhere;

            var poster = document.createElement('img');
            poster.className = 'series-item-poster';
            poster.src = ApiClient.getImageUrl(series.Id, { type: 'Primary', maxWidth: 80, quality: 80 });
            poster.onerror = function () { this.style.visibility = 'hidden'; };

            var info = document.createElement('div');
            info.className = 'series-item-info';

            var nameSpan = document.createElement('span');
            nameSpan.className = 'series-item-name';
            nameSpan.textContent = series.Name;
            info.appendChild(nameSpan);

            if (series.ProductionYear) {
                var yearSpan = document.createElement('span');
                yearSpan.className = 'series-item-year';
                yearSpan.textContent = series.ProductionYear;
                info.appendChild(yearSpan);
            }

            if (isElsewhere) {
                var badge = document.createElement('span');
                badge.className = 'series-item-badge';
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
            var checked = listContainer.querySelectorAll('.series-checkbox:checked').length;
            summaryEl.textContent = checked + ' of ' + availableCount + ' available series selected';
        }

        updateSummary();

        var searchInput = view.querySelector('#seriesSearchInput');
        searchInput.value = '';
        searchInput.focus();
    }

    var filterSeriesList = debounce(function () {
        var term = view.querySelector('#seriesSearchInput').value.toLowerCase();
        view.querySelectorAll('.series-checkbox-item').forEach(function (item) {
            var name = item.querySelector('.series-item-name');
            item.style.display = (name ? name.textContent : item.textContent).toLowerCase().includes(term) ? '' : 'none';
        });
    }, 200);

    function confirmSeriesSelection() {
        var selected = [];
        view.querySelectorAll('.series-checkbox:checked').forEach(function (cb) { selected.push(cb.value); });
        getCurrentProfile().SeriesIds = selected;
        renderAssignedSeries();
        closeSeriesSelectionModal();
        checkDirty();
    }

    function onSeriesModalKeydown(e) {
        if (e.key === 'Escape') {
            e.preventDefault();
            closeSeriesSelectionModal();
        } else {
            trapFocus(view.querySelector('.series-modal-content'), e);
        }
    }

    function closeSeriesSelectionModal() {
        view.querySelector('#seriesSelectionModal').style.display = 'none';
        document.removeEventListener('keydown', onSeriesModalKeydown);
        if (_seriesModalTrigger && _seriesModalTrigger.focus) _seriesModalTrigger.focus();
        _seriesModalTrigger = null;
    }

    function removeSeries(seriesId) {
        var profile = getCurrentProfile();
        profile.SeriesIds = profile.SeriesIds.filter(function (id) { return !sameId(id, seriesId); });
        renderAssignedSeries();
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
            return !p.IsDefault && p.Scope !== 'Movies' && (!p.SeriesIds || p.SeriesIds.length === 0);
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
        view.querySelector('#selectProfileScope').addEventListener('change', function () {
            var profile = getCurrentProfile();
            if (!profile) return;

            profile.Scope = this.value;
            loadCurrentProfile();
            checkDirty();
        });

        view.querySelector('#EpgProfilesForm').addEventListener('submit', function (e) {
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

        view.querySelector('#btnAddSeries').addEventListener('click', showSeriesSelectionModal);
        view.querySelector('#btnCancelSeriesSelection').addEventListener('click', closeSeriesSelectionModal);
        view.querySelector('#btnCloseSeriesModal').addEventListener('click', closeSeriesSelectionModal);
        view.querySelector('#btnConfirmSeriesSelection').addEventListener('click', confirmSeriesSelection);
        view.querySelector('#seriesSelectionModal').addEventListener('click', function (e) {
            if (e.target === this) closeSeriesSelectionModal();
        });
        view.querySelector('#seriesSearchInput').addEventListener('input', filterSeriesList);

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
