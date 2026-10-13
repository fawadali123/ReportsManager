// Schedule editor: shows only the settings that apply to the chosen frequency and delivery options.
(function () {
    'use strict';

    const form = document.getElementById('schedule-editor');
    if (!form) {
        return;
    }

    const frequencyInputs = form.querySelectorAll('input[name="Frequency"]');

    function currentFrequency() {
        const checked = form.querySelector('input[name="Frequency"]:checked');
        return checked ? checked.value : 'Daily';
    }

    function applyFrequency() {
        const frequency = currentFrequency();

        form.querySelectorAll('[data-frequency]').forEach((block) => {
            const applies = block.dataset.frequency.split(' ').includes(frequency);
            block.hidden = !applies;
        });

        // Labels that read differently for some frequencies.
        form.querySelectorAll('[data-label-default]').forEach((label) => {
            const specific = label.dataset['label' + frequency.charAt(0) + frequency.slice(1).toLowerCase()];
            label.textContent = specific || label.dataset.labelDefault;
        });
    }

    function applyToggle(input) {
        const target = form.querySelector(input.dataset.toggles);
        if (target) {
            target.hidden = !input.checked;
        }
    }

    frequencyInputs.forEach((input) => input.addEventListener('change', applyFrequency));

    form.querySelectorAll('input[data-toggles]').forEach((input) => {
        input.addEventListener('change', () => applyToggle(input));
        applyToggle(input);
    });

    // Quick picks for the days of the week.
    form.querySelectorAll('button[data-days]').forEach((button) => {
        button.addEventListener('click', () => {
            const days = button.dataset.days.split(' ');
            form.querySelectorAll('input[name="DaysOfWeek"]').forEach((box) => {
                box.checked = days.includes(box.value);
            });
        });
    });

    applyFrequency();
})();
