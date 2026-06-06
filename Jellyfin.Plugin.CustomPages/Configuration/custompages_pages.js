export default function (view) {
    'use strict';

    var PLUGIN_ID = '409ef72d-6014-47fd-8928-ebad581bf81b';
    var TABS = [
        { href: 'configurationpage?name=custompages_pages', name: 'Pages' },
        { href: 'configurationpage?name=custompages_assets', name: 'Assets' }
    ];

    var Shared = null;
    var setTabs = null;
    var _sharedPromise = import('/web/configurationpage?name=custompages_jpkribs_shared.js').then(function (mod) {
        Shared = mod.createShared(view, PLUGIN_ID);
        setTabs = mod.setTabs;
    });

    function slugify(value) {
        return String(value || '')
            .toLowerCase()
            .replace(/[^a-z0-9-_]+/g, '-')
            .replace(/^-+|-+$/g, '');
    }

    var VISIBILITIES = ['Anonymous', 'User', 'Admin'];

    var PANE = { html: 'Html', css: 'Css', js: 'Js' };
    var PANE_HINT = {
        html: 'HTML that will be combined with CSS and JS into a final page.',
        css: 'Styles that will be added to the final page using a <style> element.',
        js: 'JavaScript that will be added to the final page using a <script> element.'
    };
    var SINGLE_HINT = 'A complete HTML document served exactly as written.';

    var config = null;
    var pages = [];
    var currentIndex = -1;
    var currentPane = 'html';
    var editorSingle = false;
    var editorEnabled = true;
    var _bound = false;

    function el(id) { return view.querySelector('#' + id); }

    function normalizeVisibility(value) {
        if (typeof value === 'number') return VISIBILITIES[value] || 'Anonymous';
        return VISIBILITIES.indexOf(value) >= 0 ? value : 'Anonymous';
    }

    function pageOrigin() {
        return ApiClient.serverAddress ? ApiClient.serverAddress() : window.location.origin;
    }

    function renderCounts() {
        var c = { pub: 0, user: 0, admin: 0, unpublished: 0 };
        pages.forEach(function (p) {
            if (p.Enabled === false) { c.unpublished++; return; }
            var v = normalizeVisibility(p.Visibility);
            if (v === 'User') c.user++;
            else if (v === 'Admin') c.admin++;
            else c.pub++;
        });
        el('pageCounts').innerHTML =
            '<div class="jpk-card blue"><span class="jpk-card-count">' + c.pub + '</span><span class="jpk-card-label">Public</span></div>' +
            '<div class="jpk-card green"><span class="jpk-card-count">' + c.user + '</span><span class="jpk-card-label">User</span></div>' +
            '<div class="jpk-card purple"><span class="jpk-card-count">' + c.admin + '</span><span class="jpk-card-label">Admin</span></div>' +
            '<div class="jpk-card gray"><span class="jpk-card-count">' + c.unpublished + '</span><span class="jpk-card-label">Unpublished</span></div>';
    }

    function renderSelect() {
        renderCounts();
        var select = el('selectPage');
        select.innerHTML = pages.map(function (p, i) {
            var label = (p.Slug || 'new page') + (p.Enabled === false ? ', unpublished' : '');
            return '<option value="' + i + '">' + Shared.escapeHtml(label) + '</option>';
        }).join('');

        var hasPages = pages.length > 0;
        Shared.setVisible('emptyState', !hasPages);
        Shared.setVisible('pageEditor', hasPages);
        Shared.setVisible('btnDeletePage', hasPages);
        Shared.setVisible('btnPublish', hasPages);

        if (hasPages) {
            if (currentIndex < 0 || currentIndex >= pages.length) currentIndex = 0;
            select.value = String(currentIndex);
            loadEditor();
        }
    }

    function setPublishVisual(enabled) {
        var btn = el('btnPublish');
        btn.classList.toggle('jpk-button-submit', enabled);
        btn.classList.toggle('jpk-secondary', !enabled);
        el('publishLabel').textContent = enabled ? 'Published' : 'Unpublished';
        btn.querySelector('.material-icons').textContent = enabled ? 'visibility' : 'visibility_off';
    }

    function activeField() {
        return editorSingle ? 'Document' : PANE[currentPane];
    }

    function loadSource() {
        var p = pages[currentIndex];
        if (!p) return;
        el('pageSource').value = p[activeField()] || '';
        el('sourceHint').textContent = editorSingle ? SINGLE_HINT : PANE_HINT[currentPane];
    }

    function flushSource() {
        var p = pages[currentIndex];
        if (!p) return;
        p[activeField()] = el('pageSource').value;
    }

    function applyMode() {
        Shared.setVisible('sourceRow', !editorSingle);
        loadSource();
    }

    function loadEditor() {
        var p = pages[currentIndex];
        if (!p) return;
        el('pageSlug').value = p.Slug || '';
        el('pageTitle').value = p.Title || '';
        el('pageVisibility').value = normalizeVisibility(p.Visibility);

        editorEnabled = p.Enabled !== false;
        setPublishVisual(editorEnabled);

        editorSingle = !!p.SingleFile;
        el('pageSingleFile').checked = editorSingle;
        currentPane = 'html';
        el('selectSource').value = 'html';
        applyMode();
        updateUrlPreview();
    }

    function updateUrlPreview() {
        var slug = slugify(el('pageSlug').value);
        var link = el('pageUrl');
        var href = pageOrigin().replace(/\/+$/, '') + '/pages/' + (slug || '');
        link.textContent = '/pages/' + (slug || '');
        link.href = slug ? href : '#';
    }

    function readEditorInto(p) {
        flushSource();
        p.Slug = slugify(el('pageSlug').value);
        p.Title = el('pageTitle').value.trim();
        p.Visibility = el('pageVisibility').value;
        p.SingleFile = editorSingle;
        p.Enabled = editorEnabled;
    }

    function save() {
        var p = pages[currentIndex];
        if (!p) return;
        readEditorInto(p);

        if (!p.Slug) {
            Shared.setStatus('pageStatus', 'A slug is required.', true);
            return;
        }

        var clash = pages.some(function (other, i) {
            return i !== currentIndex && (other.Slug || '').toLowerCase() === p.Slug.toLowerCase();
        });
        if (clash) {
            Shared.setStatus('pageStatus', 'Another page already uses that slug.', true);
            return;
        }

        persistPages('Saved.', 'Save failed.');
    }

    function persistPages(okMessage, errMessage) {
        return Shared.getConfig().then(function (fresh) {
            fresh.Pages = pages;
            config = fresh;
            return Shared.saveConfig(fresh);
        }).then(function () {
            renderSelect();
            Shared.setStatus('pageStatus', okMessage, false);
        }).catch(function () {
            Shared.setStatus('pageStatus', errMessage, true);
        });
    }

    function addPage() {
        pages.push({ Slug: '', Title: '', Visibility: 'Anonymous', Html: '', Css: '', Js: '', Document: '', SingleFile: false, Enabled: true });
        currentIndex = pages.length - 1;
        currentPane = 'html';
        editorSingle = false;
        renderSelect();
        el('pageSlug').focus();
    }

    function deletePage() {
        var p = pages[currentIndex];
        if (!p) return;
        var label = p.Slug || 'this page';
        if (!window.confirm('Delete /pages/' + label + '? This cannot be undone.')) return;

        pages.splice(currentIndex, 1);
        currentIndex = -1;
        persistPages('Deleted.', 'Delete failed.');
    }

    function bind() {
        el('selectPage').addEventListener('change', function () {
            currentIndex = parseInt(this.value, 10);
            loadEditor();
        });
        el('pageSingleFile').addEventListener('change', function () {
            flushSource();
            editorSingle = this.checked;
            applyMode();
        });
        el('selectSource').addEventListener('change', function () {
            flushSource();
            currentPane = this.value;
            loadSource();
        });
        el('btnNewPage').addEventListener('click', addPage);
        el('btnDeletePage').addEventListener('click', deletePage);
        el('btnSavePage').addEventListener('click', save);
        el('btnPublish').addEventListener('click', function () {
            editorEnabled = !editorEnabled;
            setPublishVisual(editorEnabled);
        });
        el('pageSlug').addEventListener('input', updateUrlPreview);
    }

    function load() {
        Shared.getConfig().then(function (cfg) {
            config = cfg;
            pages = config.Pages || [];
            currentIndex = pages.length ? 0 : -1;
            renderSelect();
        });
    }

    view.addEventListener('viewshow', function () {
        _sharedPromise.then(function () {
            setTabs('custompages', 0, TABS);
            if (!_bound) { bind(); _bound = true; }
            load();
        });
    });
}
