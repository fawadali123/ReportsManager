/* Reports screen: application / category / report pickers, the generated parameter form, the result grid and export. */
(function () {
    'use strict';

    const dataElement = document.getElementById('report-catalog');
    if (!dataElement) {
        return; // no reports available to this user
    }

    const { el, request, readError } = window.RMS;
    const config = JSON.parse(dataElement.textContent);
    const reports = config.reports;
    const byName = (a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base', numeric: true });

    const ui = {
        application: document.getElementById('pick-application'),
        category: document.getElementById('pick-category'),
        report: document.getElementById('pick-report'),
        placeholder: document.getElementById('report-placeholder'),
        area: document.getElementById('report-area'),
        title: document.getElementById('report-title'),
        description: document.getElementById('report-description'),
        editLink: document.getElementById('report-edit-link'),
        form: document.getElementById('report-form'),
        fields: document.getElementById('report-fields'),
        noParameters: document.getElementById('report-no-parameters'),
        runButton: document.getElementById('run-button'),
        exportButtons: [...document.querySelectorAll('[data-export]')],
        message: document.getElementById('report-message'),
        result: document.getElementById('report-result'),
        summary: document.getElementById('result-summary'),
        filter: document.getElementById('result-filter'),
        grid: document.getElementById('result-grid'),
        gridScroll: document.querySelector('.rms-grid-scroll'),
        empty: document.getElementById('result-empty'),
        pageSize: document.getElementById('result-page-size'),
        range: document.getElementById('result-range'),
        previous: document.getElementById('result-prev'),
        next: document.getElementById('result-next'),
        pager: document.querySelector('.rms-pager'),
    };

    let current = null;       // definition of the selected report
    let loadToken = 0;        // guards against a slow response for a report the user has already left
    const grid = { columns: [], rows: [], view: [], sortIndex: -1, sortDirection: 1, page: 0 };

    // ------------------------------------------------------------------ pickers

    function distinct(items, idKey, nameKey) {
        const seen = new Map();
        for (const item of items) {
            if (!seen.has(item[idKey])) {
                seen.set(item[idKey], { id: item[idKey], name: item[nameKey] });
            }
        }
        return [...seen.values()].sort(byName);
    }

    function fill(select, items, placeholder) {
        select.replaceChildren(
            el('option', { value: '', text: placeholder }),
            ...items.map((item) => el('option', { value: String(item.id), text: item.name })));
        select.disabled = items.length === 0;
        // With a single choice there is nothing to decide, so make it for the user.
        if (items.length === 1) {
            select.value = String(items[0].id);
        }
    }

    function reportsFor(applicationId, categoryId) {
        return reports
            .filter((r) => r.applicationId === applicationId && r.categoryId === categoryId)
            .map((r) => ({ id: r.id, name: r.available ? r.name : `${r.name} (inactive)` }))
            .sort(byName);
    }

    function onApplicationChanged() {
        const applicationId = Number(ui.application.value);
        const categories = applicationId
            ? distinct(reports.filter((r) => r.applicationId === applicationId), 'categoryId', 'categoryName')
            : [];
        fill(ui.category, categories, applicationId ? 'Choose a category' : 'Choose an application first');
        onCategoryChanged();
    }

    function onCategoryChanged() {
        const applicationId = Number(ui.application.value);
        const categoryId = Number(ui.category.value);
        const items = applicationId && categoryId ? reportsFor(applicationId, categoryId) : [];
        fill(ui.report, items, categoryId ? 'Choose a report' : 'Choose a category first');
        onReportChanged();
    }

    function onReportChanged() {
        loadReport(Number(ui.report.value) || null);
    }

    function selectReport(reportId) {
        const report = reports.find((r) => r.id === reportId);
        if (!report) {
            return false;
        }
        ui.application.value = String(report.applicationId);
        onApplicationChanged();
        ui.category.value = String(report.categoryId);
        onCategoryChanged();
        ui.report.value = String(report.id);
        onReportChanged();
        return true;
    }

    // ------------------------------------------------------------------ parameter form

    async function loadReport(reportId) {
        const token = ++loadToken;
        current = null;
        ui.area.hidden = true;
        ui.placeholder.hidden = false;
        hideMessage();
        clearResult();

        const url = new URL(window.location.href);
        if (reportId) {
            url.searchParams.set('reportId', reportId);
        } else {
            url.searchParams.delete('reportId');
        }
        window.history.replaceState(null, '', url);

        if (!reportId) {
            return;
        }

        let response;
        try {
            response = await request(`${config.definitionUrl}/${reportId}`);
        } catch {
            if (token === loadToken) {
                showMessage('danger', 'The server could not be reached. Check your connection and try again.');
                ui.placeholder.hidden = true;
                ui.area.hidden = false;
                ui.form.hidden = true;
            }
            return;
        }
        if (token !== loadToken) {
            return;
        }

        ui.placeholder.hidden = true;
        ui.area.hidden = false;

        if (!response.ok) {
            ui.form.hidden = true;
            ui.title.textContent = '';
            ui.description.textContent = '';
            showMessage('danger', (await readError(response)).error);
            return;
        }

        current = await response.json();
        renderForm(current);
    }

    function renderForm(definition) {
        ui.form.hidden = false;
        ui.title.textContent = definition.name;
        ui.description.textContent = definition.description ?? '';
        ui.description.hidden = !definition.description;

        if (ui.editLink) {
            ui.editLink.href = ui.editLink.dataset.hrefTemplate.replace(/0$/, String(definition.id));
        }

        ui.fields.replaceChildren(...definition.fields.map(renderField));
        ui.noParameters.hidden = definition.fields.length > 0;

        if (!definition.available) {
            showMessage('warning', 'This report is inactive (or its application or category is). Only administrators can open it.');
        }
    }

    function renderField(field, index) {
        const id = `param-${index}`;
        const name = `p.${field.name}`;
        const isChecklist = field.options && field.multiple;

        const label = el(isChecklist ? 'span' : 'label', { class: 'form-label d-block', text: field.label });
        if (isChecklist) {
            label.id = `${id}-label`;
        } else {
            label.htmlFor = id;
        }
        if (field.required) {
            label.append(el('span', { class: 'rms-required', text: '*', title: 'Required' }));
        } else {
            label.append(el('span', { class: 'rms-optional', text: '(optional)' }));
        }

        const control = isChecklist ? checklist(field, id, name) : input(field, id, name);
        const wrapper = el('div', { class: 'col-sm-6 col-md-4 col-xl-3', 'data-param': field.name.toLowerCase() },
            label,
            control,
            field.help ? el('div', { class: 'rms-field-help', text: field.help }) : null,
            el('div', { class: 'invalid-feedback d-block', text: field.lookupError ?? '' }));

        return wrapper;
    }

    function input(field, id, name) {
        const blank = field.required ? 'Choose' : '(any)';

        if (field.options) {
            return el('select', { id, name, class: 'form-select' },
                el('option', { value: '', text: blank }),
                ...field.options.map((option) => el('option', {
                    value: option.value,
                    text: option.label,
                    selected: option.value === field.defaultValue,
                })));
        }

        if (field.type === 'boolean') {
            const value = normalizeBoolean(field.defaultValue);
            return el('select', { id, name, class: 'form-select' },
                el('option', { value: '', text: blank }),
                el('option', { value: 'true', text: 'Yes', selected: value === 'true' }),
                el('option', { value: 'false', text: 'No', selected: value === 'false' }));
        }

        const types = {
            date: { type: 'date' },
            datetime: { type: 'datetime-local' },
            integer: { type: 'number', step: '1', inputMode: 'numeric' },
            decimal: { type: 'number', step: 'any', inputMode: 'decimal' },
            text: { type: 'text', maxLength: 4000 },
        };

        return el('input', {
            ...(types[field.type] ?? types.text),
            id,
            name,
            class: 'form-control',
            value: field.defaultValue ?? '',
            autocomplete: 'off',
        });
    }

    function normalizeBoolean(value) {
        const text = String(value ?? '').toLowerCase();
        if (['true', '1', 'yes', 'y'].includes(text)) {
            return 'true';
        }
        if (['false', '0', 'no', 'n'].includes(text)) {
            return 'false';
        }
        return '';
    }

    /** Multi-select: a dropdown holding a list of checkboxes, with a filter once the list gets long. */
    function checklist(field, id, name) {
        const defaults = new Set(String(field.defaultValue ?? '').split(',').map((v) => v.trim()).filter(Boolean));
        const blank = field.required ? 'Choose' : '(any)';

        const boxes = field.options.map((option, i) => el('input', {
            type: 'checkbox',
            class: 'form-check-input',
            id: `${id}-${i}`,
            name,
            value: option.value,
            checked: defaults.has(option.value),
        }));

        const items = el('div', { class: 'rms-multi-items' },
            ...field.options.map((option, i) => el('div', { class: 'form-check', 'data-search': option.label.toLowerCase() },
                boxes[i],
                el('label', { class: 'form-check-label', htmlFor: `${id}-${i}`, text: option.label }))));

        if (field.options.length === 0) {
            items.append(el('span', { class: 'rms-field-help', text: 'No choices available.' }));
        }

        const toggle = el('button', {
            type: 'button',
            id,
            class: 'form-select rms-multi-toggle',
            'data-bs-toggle': 'dropdown',
            'data-bs-auto-close': 'outside',
            'aria-expanded': 'false',
            'aria-labelledby': `${id}-label ${id}`,
        });

        const summarize = () => {
            const chosen = field.options.filter((_, i) => boxes[i].checked).map((option) => option.label);
            toggle.textContent = chosen.length === 0
                ? blank
                : chosen.length <= 2 ? chosen.join(', ') : `${chosen.length} selected`;
            toggle.title = chosen.join(', ');
        };

        const setAll = (checked) => {
            boxes.forEach((box) => {
                if (!box.closest('.form-check').hidden) {
                    box.checked = checked;
                }
            });
            summarize();
        };

        const menu = el('div', { class: 'dropdown-menu rms-multi-menu' });

        if (field.options.length > 8) {
            const search = el('input', {
                type: 'search',
                class: 'form-control form-control-sm rms-multi-filter',
                placeholder: `Filter ${field.options.length} choices`,
                'aria-label': `Filter ${field.label}`,
            });
            search.addEventListener('input', () => {
                const query = search.value.trim().toLowerCase();
                for (const item of items.children) {
                    item.hidden = query.length > 0 && !(item.dataset.search ?? '').includes(query);
                }
            });
            menu.append(search);
        }

        const selectAll = el('button', { type: 'button', class: 'btn btn-sm btn-link', text: 'Select all' });
        const clearAll = el('button', { type: 'button', class: 'btn btn-sm btn-link', text: 'Clear' });
        selectAll.addEventListener('click', () => setAll(true));
        clearAll.addEventListener('click', () => setAll(false));

        menu.append(items, el('div', { class: 'rms-multi-actions' }, selectAll, clearAll));
        items.addEventListener('change', summarize);
        summarize();

        return el('div', { class: 'dropdown rms-multi' }, toggle, menu);
    }

    // ------------------------------------------------------------------ messages

    function showMessage(kind, text) {
        ui.message.className = `alert alert-${kind}`;
        ui.message.textContent = text;
        ui.message.hidden = false;
    }

    function hideMessage() {
        ui.message.hidden = true;
        ui.message.textContent = '';
    }

    function clearFieldErrors() {
        ui.fields.querySelectorAll('[data-param]').forEach((wrapper) => {
            wrapper.querySelectorAll('.is-invalid').forEach((node) => node.classList.remove('is-invalid'));
            wrapper.querySelector('.invalid-feedback').textContent = '';
        });
    }

    function showFieldErrors(fieldErrors) {
        let first = null;
        for (const [name, text] of Object.entries(fieldErrors)) {
            const wrapper = ui.fields.querySelector(`[data-param="${CSS.escape(name.toLowerCase())}"]`);
            if (!wrapper) {
                continue;
            }
            const control = wrapper.querySelector('.form-select, .form-control');
            control?.classList.add('is-invalid');
            wrapper.querySelector('.invalid-feedback').textContent = text;
            first ??= wrapper.querySelector('.form-select, .form-control');
        }
        first?.focus();
    }

    async function showFailure(response) {
        const failure = await readError(response);
        showFieldErrors(failure.fieldErrors);
        showMessage('danger', failure.error);
    }

    function setBusy(button, busy) {
        [ui.runButton, ...ui.exportButtons].forEach((b) => { b.disabled = busy; });
        const spinner = button.querySelector('.spinner-border');
        if (busy && !spinner) {
            button.prepend(el('span', { class: 'spinner-border spinner-border-sm me-1', role: 'status', 'aria-hidden': 'true' }));
        } else if (!busy && spinner) {
            spinner.remove();
        }
    }

    // ------------------------------------------------------------------ run

    async function run() {
        if (!current) {
            return;
        }
        const reportId = current.id;
        hideMessage();
        clearFieldErrors();
        setBusy(ui.runButton, true);

        try {
            const response = await request(`${config.runUrl}/${reportId}`, { method: 'POST', body: new FormData(ui.form) });
            if (!current || current.id !== reportId) {
                return; // the user moved on while the query was running
            }
            if (response.ok) {
                showResult(await response.json());
            } else {
                clearResult();
                await showFailure(response);
            }
        } catch {
            showMessage('danger', 'The server could not be reached. Check your connection and try again.');
        } finally {
            setBusy(ui.runButton, false);
        }
    }

    // ------------------------------------------------------------------ export

    async function exportReport(button) {
        if (!current) {
            return;
        }
        hideMessage();
        clearFieldErrors();
        setBusy(button, true);

        try {
            const url = `${config.exportUrl}/${current.id}?format=${encodeURIComponent(button.dataset.export)}`;
            const response = await request(url, { method: 'POST', body: new FormData(ui.form) });
            if (!response.ok) {
                await showFailure(response);
                return;
            }

            const blob = await response.blob();
            const link = el('a', {
                href: URL.createObjectURL(blob),
                download: response.headers.get('X-File-Name') ?? `report.${button.dataset.export}`,
            });
            document.body.append(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(link.href), 60000);

            if (response.headers.get('X-Report-Truncated') === 'true') {
                const rows = Number(response.headers.get('X-Row-Count') ?? 0).toLocaleString();
                showMessage('warning', `The ${button.textContent.trim()} file stops at ${rows} rows because the report has more than this format allows. Narrow the parameters, or use CSV for the full result.`);
            }
        } catch {
            showMessage('danger', 'The download did not complete. Check your connection and try again.');
        } finally {
            setBusy(button, false);
        }
    }

    // ------------------------------------------------------------------ result grid

    function clearResult() {
        ui.result.hidden = true;
        grid.columns = [];
        grid.rows = [];
        grid.view = [];
        ui.grid.tHead.replaceChildren();
        ui.grid.tBodies[0].replaceChildren();
    }

    function showResult(data) {
        grid.columns = data.columns;
        grid.rows = data.rows;
        grid.sortIndex = -1;
        grid.sortDirection = 1;
        grid.page = 0;
        ui.filter.value = '';

        const seconds = (data.elapsedMs / 1000).toFixed(data.elapsedMs < 10000 ? 1 : 0);
        const rows = data.rowCount.toLocaleString();
        ui.summary.textContent = data.truncated
            ? `Showing the first ${rows} rows (${seconds} s). Download the report to get every row.`
            : `${rows} ${data.rowCount === 1 ? 'row' : 'rows'} in ${seconds} s`;

        ui.result.hidden = false;
        renderHeader();
        applyView();
        ui.result.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }

    function renderHeader() {
        ui.grid.tHead.replaceChildren(el('tr', {}, ...grid.columns.map((column, index) => {
            const isSorted = grid.sortIndex === index;
            const button = el('button', { type: 'button', title: `Sort by ${column.name}` },
                el('span', { text: column.name }),
                el('span', { class: 'rms-sort', 'aria-hidden': 'true', text: isSorted ? (grid.sortDirection > 0 ? '▲' : '▼') : '' }));
            button.addEventListener('click', () => sortBy(index));

            return el('th', {
                scope: 'col',
                class: column.type === 'number' ? 'is-number' : '',
                'aria-sort': isSorted ? (grid.sortDirection > 0 ? 'ascending' : 'descending') : 'none',
            }, button);
        })));
    }

    function sortBy(index) {
        if (grid.sortIndex === index) {
            grid.sortDirection = -grid.sortDirection;
        } else {
            grid.sortIndex = index;
            grid.sortDirection = 1;
        }
        grid.page = 0;
        renderHeader();
        applyView();
    }

    function compare(type) {
        if (type === 'number') {
            return (a, b) => Number(a) - Number(b);
        }
        // Dates arrive as YYYY-MM-DD HH:MM:SS, which sorts correctly as text.
        return (a, b) => a.localeCompare(b, undefined, { sensitivity: 'base', numeric: type !== 'date' });
    }

    function applyView() {
        const query = ui.filter.value.trim().toLowerCase();
        let view = query
            ? grid.rows.filter((row) => row.some((cell) => cell.toLowerCase().includes(query)))
            : grid.rows.slice();

        if (grid.sortIndex >= 0) {
            const index = grid.sortIndex;
            const order = compare(grid.columns[index].type);
            const direction = grid.sortDirection;
            view.sort((x, y) => {
                const a = x[index];
                const b = y[index];
                if (a === '' || b === '') {
                    return a === b ? 0 : a === '' ? 1 : -1; // blanks always last
                }
                return order(a, b) * direction;
            });
        }

        grid.view = view;
        renderPage();
    }

    function renderPage() {
        const size = Number(ui.pageSize.value);
        const total = grid.view.length;
        const pages = Math.max(1, Math.ceil(total / size));
        grid.page = Math.min(grid.page, pages - 1);
        const start = grid.page * size;
        const end = Math.min(start + size, total);

        ui.grid.tBodies[0].replaceChildren(...grid.view.slice(start, end).map((row) =>
            el('tr', {}, ...row.map((cell, index) => el('td', {
                class: grid.columns[index].type === 'number' ? 'is-number' : '',
                text: cell,
                title: cell.length > 40 ? cell : null,
            })))));

        const isEmpty = total === 0;
        ui.empty.hidden = !isEmpty;
        ui.empty.textContent = grid.rows.length === 0
            ? 'No rows match these parameters.'
            : 'No rows match the filter.';
        ui.gridScroll.hidden = grid.columns.length === 0 || (isEmpty && grid.rows.length === 0);
        ui.pager.hidden = isEmpty;

        ui.range.textContent = isEmpty
            ? ''
            : `Rows ${(start + 1).toLocaleString()} to ${end.toLocaleString()} of ${total.toLocaleString()}`
              + (total !== grid.rows.length ? ` (filtered from ${grid.rows.length.toLocaleString()})` : '');
        ui.previous.disabled = grid.page === 0;
        ui.next.disabled = grid.page >= pages - 1;
        ui.gridScroll.scrollTop = 0;
    }

    // ------------------------------------------------------------------ wiring

    ui.application.addEventListener('change', onApplicationChanged);
    ui.category.addEventListener('change', onCategoryChanged);
    ui.report.addEventListener('change', onReportChanged);

    ui.form.addEventListener('submit', (event) => {
        event.preventDefault();
        run();
    });

    ui.exportButtons.forEach((button) => button.addEventListener('click', () => exportReport(button)));

    // An error under a field goes away as soon as the user changes that field.
    const clearOwnError = (event) => {
        const wrapper = event.target.closest('[data-param]');
        if (wrapper) {
            wrapper.querySelectorAll('.is-invalid').forEach((node) => node.classList.remove('is-invalid'));
            wrapper.querySelector('.invalid-feedback').textContent = '';
        }
    };
    ui.fields.addEventListener('input', clearOwnError);
    ui.fields.addEventListener('change', clearOwnError);

    ui.filter.addEventListener('input', () => {
        grid.page = 0;
        applyView();
    });
    ui.pageSize.addEventListener('change', () => {
        grid.page = 0;
        renderPage();
    });
    ui.previous.addEventListener('click', () => {
        grid.page -= 1;
        renderPage();
    });
    ui.next.addEventListener('click', () => {
        grid.page += 1;
        renderPage();
    });

    fill(ui.application, distinct(reports, 'applicationId', 'applicationName'), 'Choose an application');
    if (!(config.selectedReportId && selectReport(config.selectedReportId))) {
        onApplicationChanged();
    }
})();
