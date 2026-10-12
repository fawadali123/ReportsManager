/* Assign reports screen: filter, running count, select-all and per-application toggles. */
(function () {
    'use strict';

    const form = document.getElementById('assignment-form');
    if (!form) {
        return;
    }

    const filter = document.getElementById('assignment-filter');
    const count = document.getElementById('assignment-count');
    const boxes = () => [...form.querySelectorAll('input[name="reportIds"]')];
    const isShown = (box) => !box.closest('li').hidden;

    function refresh() {
        const all = boxes();
        const selected = all.filter((box) => box.checked).length;
        count.textContent = `${selected} of ${all.length} selected`;

        form.querySelectorAll('.rms-assign-app').forEach((group) => {
            const groupBoxes = [...group.querySelectorAll('input[name="reportIds"]')];
            const checked = groupBoxes.filter((box) => box.checked).length;
            const toggle = group.querySelector('[data-group-toggle]');
            toggle.checked = checked > 0 && checked === groupBoxes.length;
            toggle.indeterminate = checked > 0 && checked < groupBoxes.length;
        });
    }

    filter.addEventListener('input', () => {
        const query = filter.value.trim().toLowerCase();
        form.querySelectorAll('li[data-search]').forEach((item) => {
            item.hidden = query.length > 0 && !item.dataset.search.includes(query);
        });
        form.querySelectorAll('.rms-assign-cat').forEach((category) => {
            category.hidden = !category.querySelector('li:not([hidden])');
        });
        form.querySelectorAll('.rms-assign-app').forEach((group) => {
            group.hidden = !group.querySelector('li:not([hidden])');
        });
    });

    form.addEventListener('change', (event) => {
        const target = event.target;
        if (target.matches('[data-group-toggle]')) {
            target.closest('.rms-assign-app').querySelectorAll('input[name="reportIds"]').forEach((box) => {
                if (isShown(box)) {
                    box.checked = target.checked;
                }
            });
        }
        refresh();
    });

    form.querySelectorAll('[data-select-all]').forEach((button) => {
        button.addEventListener('click', () => {
            const value = button.dataset.selectAll === 'true';
            boxes().filter(isShown).forEach((box) => { box.checked = value; });
            refresh();
        });
    });

    refresh();
})();
