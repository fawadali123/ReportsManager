/* Report editor: keeps the parameter settings in step with the @parameters found in the SQL. */
(function () {
    'use strict';

    const dataElement = document.getElementById('report-editor-data');
    if (!dataElement) {
        return;
    }

    const { el, request } = window.RMS;
    const config = JSON.parse(dataElement.textContent);

    const list = document.getElementById('parameter-list');
    const emptyNote = document.getElementById('parameter-empty');
    const sql = document.getElementById('SqlQuery');
    const queryType = document.getElementById('QueryType');
    const application = document.getElementById('ApplicationId');
    const databaseHint = document.getElementById('application-database');
    const inspectButton = document.getElementById('inspect-sql');
    const inspectResult = document.getElementById('inspect-result');

    let parameters = config.parameters.map((p) => ({ ...p, isNew: false }));

    const fieldName = (index, property) => `Parameters[${index}].${property}`;
    const isDate = (dataType) => dataType === 'Date' || dataType === 'DateTime';

    // Suggestions for the default value of date parameters.
    const tokens = el('datalist', { id: 'rms-date-tokens' },
        ...config.dateTokens.map((token) => el('option', { value: token })));
    document.body.append(tokens);

    /** Copies what is on screen back into the model, so nothing typed is lost when rows are redrawn. */
    function readRows() {
        parameters = [...list.querySelectorAll('.rms-param')].map((row) => {
            const value = (property) => row.querySelector(`[data-field="${property}"]`);
            return {
                name: row.dataset.name,
                label: value('Label').value,
                dataType: value('DataType').value,
                required: value('Required').checked,
                allowMultiple: value('AllowMultiple').checked,
                defaultValue: value('DefaultValue').value,
                lookupSource: value('LookupSource').value,
                helpText: value('HelpText').value,
                isNew: row.classList.contains('is-new'),
            };
        });
    }

    function render() {
        list.replaceChildren(...parameters.map(renderRow));
        emptyNote.hidden = parameters.length > 0;
    }

    function renderRow(parameter, index) {
        const id = (property) => `parameter-${index}-${property}`;

        const typeSelect = el('select', { id: id('DataType'), name: fieldName(index, 'DataType'), class: 'form-select form-select-sm', 'data-field': 'DataType' },
            ...config.dataTypes.map((type) => el('option', { value: type.value, text: type.label, selected: type.value === parameter.dataType })));

        const defaultInput = el('input', {
            type: 'text', id: id('DefaultValue'), name: fieldName(index, 'DefaultValue'), class: 'form-control form-control-sm',
            value: parameter.defaultValue ?? '', maxLength: 500, autocomplete: 'off', 'data-field': 'DefaultValue',
        });

        const syncDefaultHint = () => {
            if (isDate(typeSelect.value)) {
                defaultInput.setAttribute('list', tokens.id);
                defaultInput.placeholder = 'TODAY, START_OF_MONTH, TODAY-7 or 2026-01-31';
            } else {
                defaultInput.removeAttribute('list');
                defaultInput.placeholder = '';
            }
        };
        typeSelect.addEventListener('change', syncDefaultHint);
        syncDefaultHint();

        const move = (offset) => {
            readRows();
            const target = index + offset;
            [parameters[index], parameters[target]] = [parameters[target], parameters[index]];
            render();
            list.querySelectorAll('.rms-param')[target]?.querySelector(offset < 0 ? '[data-move="up"]' : '[data-move="down"]')?.focus();
        };

        const up = el('button', { type: 'button', class: 'btn btn-outline-secondary', title: 'Move up', 'aria-label': `Move ${parameter.name} up`, disabled: index === 0, 'data-move': 'up' },
            el('i', { class: 'bi bi-arrow-up', 'aria-hidden': 'true' }));
        const down = el('button', { type: 'button', class: 'btn btn-outline-secondary', title: 'Move down', 'aria-label': `Move ${parameter.name} down`, disabled: index === parameters.length - 1, 'data-move': 'down' },
            el('i', { class: 'bi bi-arrow-down', 'aria-hidden': 'true' }));
        up.addEventListener('click', () => move(-1));
        down.addEventListener('click', () => move(1));

        const check = (property, text, checked) => el('div', { class: 'form-check' },
            el('input', { type: 'checkbox', class: 'form-check-input', id: id(property), name: fieldName(index, property), value: 'true', checked, 'data-field': property }),
            el('label', { class: 'form-check-label', htmlFor: id(property), text }));

        const field = (columns, property, text, control, help) => el('div', { class: columns },
            el('label', { class: 'form-label', htmlFor: id(property), text }),
            control,
            help ? el('div', { class: 'form-text', text: help }) : null);

        return el('div', { class: `rms-param${parameter.isNew ? ' is-new' : ''}`, 'data-name': parameter.name },
            el('input', { type: 'hidden', name: fieldName(index, 'Name'), value: parameter.name }),
            el('input', { type: 'hidden', name: fieldName(index, 'SortOrder'), value: String((index + 1) * 10) }),

            el('div', { class: 'rms-param-head' },
                el('span', { class: 'rms-param-name', text: `@${parameter.name}` }),
                parameter.isNew ? el('span', { class: 'rms-param-new', text: 'New in the SQL. Review its settings.' }) : null,
                el('div', { class: 'btn-group btn-group-sm rms-param-move', role: 'group', 'aria-label': 'Order on the form' }, up, down)),

            el('div', { class: 'row g-2' },
                field('col-md-4', 'Label', 'Label',
                    el('input', { type: 'text', id: id('Label'), name: fieldName(index, 'Label'), class: 'form-control form-control-sm', value: parameter.label ?? '', maxLength: 200, 'data-field': 'Label' })),
                field('col-md-3', 'DataType', 'Type', typeSelect),
                field('col-md-3', 'DefaultValue', 'Default value', defaultInput),
                el('div', { class: 'col-md-2 d-flex align-items-end pb-1' }, check('Required', 'Required', parameter.required)),

                field('col-md-6', 'LookupSource', 'Choices',
                    el('textarea', {
                        id: id('LookupSource'), name: fieldName(index, 'LookupSource'), class: 'form-control form-control-sm', rows: 2,
                        value: parameter.lookupSource ?? '', placeholder: 'None: the user types a value', 'data-field': 'LookupSource',
                    })),
                el('div', { class: 'col-md-6' },
                    el('label', { class: 'form-label', htmlFor: id('HelpText'), text: 'Hint shown under the field' }),
                    el('input', { type: 'text', id: id('HelpText'), name: fieldName(index, 'HelpText'), class: 'form-control form-control-sm mb-2', value: parameter.helpText ?? '', maxLength: 500, 'data-field': 'HelpText' }),
                    check('AllowMultiple', 'Allow several values', parameter.allowMultiple))));
    }

    async function inspect() {
        readRows();
        inspectButton.disabled = true;
        inspectResult.className = '';
        inspectResult.textContent = 'Checking';

        try {
            const body = new FormData();
            body.append('sql', sql.value);
            body.append('queryType', queryType.value);
            body.append('applicationId', application.value);

            const response = await request(config.inspectUrl, { method: 'POST', body });
            if (!response.ok) {
                throw new Error(`status ${response.status}`);
            }
            const result = await response.json();

            const existing = new Map(parameters.map((p) => [p.name.toLowerCase(), p]));
            const found = new Map(result.parameters.map((p) => [p.name.toLowerCase(), p]));

            // Keep the current order for parameters that are still there, then add new ones in SQL order.
            const kept = parameters.filter((p) => found.has(p.name.toLowerCase()))
                .map((p) => ({ ...p, name: found.get(p.name.toLowerCase()).name }));
            const added = result.parameters.filter((p) => !existing.has(p.name.toLowerCase()))
                .map((p) => ({
                    name: p.name, label: p.label, dataType: p.dataType, required: true, allowMultiple: false,
                    defaultValue: '', lookupSource: '', helpText: '', isNew: true,
                }));
            const removed = parameters.length - kept.length;

            parameters = [...kept, ...added];
            render();

            if (result.errors.length > 0) {
                inspectResult.className = 'is-bad';
                inspectResult.textContent = result.errors.join(' ');
            } else {
                const parts = [`The SQL looks fine for ${result.database}: ${parameters.length} ${parameters.length === 1 ? 'parameter' : 'parameters'}`];
                if (added.length > 0) {
                    parts.push(`${added.length} new`);
                }
                if (removed > 0) {
                    parts.push(`${removed} removed`);
                }
                inspectResult.className = 'is-ok';
                inspectResult.textContent = parts.join(', ') + '. It is run against the database only when you run the report.';
            }
        } catch {
            inspectResult.className = 'is-bad';
            inspectResult.textContent = 'The SQL could not be checked. Try again.';
        } finally {
            inspectButton.disabled = false;
        }
    }

    /** Says which database the chosen application's reports run on, since that decides the SQL dialect. */
    function showDatabase() {
        const target = config.applications[application.value];
        databaseHint.textContent = target
            ? `Runs on ${target.connection} (${target.database}). Write the SQL for ${target.database}.`
            : '';
    }

    application.addEventListener('change', () => {
        showDatabase();
        inspectResult.className = '';
        inspectResult.textContent = '';
    });

    inspectButton.addEventListener('click', inspect);
    showDatabase();
    render();
})();
