/* Shared behaviour for every page: AJAX helper, table filter, delete confirmation. */
(function () {
    'use strict';

    const RMS = window.RMS = window.RMS || {};

    const meta = (name) => document.querySelector(`meta[name="${name}"]`)?.content ?? '';

    /**
     * fetch() with the antiforgery token and the header that tells the server this is an AJAX call.
     * If the session has expired the browser is sent to the sign-in page and the promise never settles.
     */
    RMS.request = async function (url, options = {}) {
        const method = options.method ?? 'GET';
        const headers = { 'X-Requested-With': 'XMLHttpRequest' };
        if (method !== 'GET') {
            headers['RequestVerificationToken'] = meta('csrf-token');
        }

        const response = await fetch(url, { method, body: options.body, headers, credentials: 'same-origin' });

        if (response.status === 401) {
            const returnUrl = encodeURIComponent(window.location.pathname + window.location.search);
            window.location.assign(`${meta('app-base')}Account/Login?returnUrl=${returnUrl}`);
            return new Promise(() => { });
        }
        return response;
    };

    /** Reads { error, fieldErrors } from a failed response, with a fallback for non-JSON bodies. */
    RMS.readError = async function (response) {
        try {
            const body = await response.clone().json();
            if (body && typeof body.error === 'string') {
                return { error: body.error, fieldErrors: body.fieldErrors ?? {} };
            }
        } catch {
            // not JSON; fall through
        }

        const error = response.status === 403
            ? 'Your account is not allowed to do this.'
            : `The server could not complete the request (status ${response.status}). Try again.`;
        return { error, fieldErrors: {} };
    };

    /** Small DOM builder: el('td', { class: 'x', text: 'y' }, child, child). Text is never parsed as HTML. */
    RMS.el = function (tag, properties = {}, ...children) {
        const node = document.createElement(tag);
        for (const [key, value] of Object.entries(properties)) {
            if (value === null || value === undefined || value === false) {
                continue;
            }
            if (key === 'text') {
                node.textContent = value;
            } else if (key === 'class') {
                node.className = value;
            } else if (key in node && key !== 'list') {
                node[key] = value;
            } else {
                node.setAttribute(key, value === true ? '' : value);
            }
        }
        node.append(...children.filter((child) => child !== null && child !== undefined));
        return node;
    };

    // ---- Filter box above a table: <input data-table-filter="#table-id"> ----
    document.querySelectorAll('[data-table-filter]').forEach((input) => {
        const table = document.querySelector(input.dataset.tableFilter);
        if (!table) {
            return;
        }
        input.addEventListener('input', () => {
            const query = input.value.trim().toLowerCase();
            for (const row of table.tBodies[0].rows) {
                row.hidden = query.length > 0 && !row.textContent.toLowerCase().includes(query);
            }
        });
    });

    // ---- <select data-auto-submit> submits its form when changed ----
    document.querySelectorAll('select[data-auto-submit]').forEach((select) => {
        select.addEventListener('change', () => select.form.submit());
    });

    // ---- <form data-confirm="question"> asks before submitting ----
    const dialog = document.getElementById('rms-confirm');
    if (dialog && window.bootstrap) {
        const modal = window.bootstrap.Modal.getOrCreateInstance(dialog);
        const message = document.getElementById('rms-confirm-message');
        const confirmButton = document.getElementById('rms-confirm-ok');
        let pendingForm = null;

        document.addEventListener('submit', (event) => {
            const form = event.target;
            if (!(form instanceof HTMLFormElement) || !form.dataset.confirm) {
                return;
            }
            event.preventDefault();
            pendingForm = form;
            message.textContent = form.dataset.confirm;
            modal.show();
        });

        confirmButton.addEventListener('click', () => {
            if (pendingForm) {
                confirmButton.disabled = true;
                pendingForm.submit(); // form.submit() does not raise the submit event again
            }
        });

        dialog.addEventListener('hidden.bs.modal', () => {
            pendingForm = null;
            confirmButton.disabled = false;
        });
    }
})();
