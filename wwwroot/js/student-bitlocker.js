// Loaded after the Bootstrap bundle by the Razor Scripts section.
(() => {
    'use strict';
    const element = document.getElementById('bitlocker-modal');
    if (!element) return;
    const modal = bootstrap.Modal.getOrCreateInstance(element);
    const status = document.getElementById('bitlocker-status');
    const error = document.getElementById('bitlocker-error');
    const selection = document.getElementById('bitlocker-selection');
    const select = document.getElementById('bitlocker-key-select');
    const fetchButton = document.getElementById('bitlocker-fetch');
    const copyButton = document.getElementById('bitlocker-copy');
    const valuePanel = document.getElementById('bitlocker-value-panel');
    const value = document.getElementById('bitlocker-value');
    const deviceName = document.getElementById('bitlocker-device-name');
    const studentId = document.getElementById('bitlocker-student-id').value;
    const token = element.querySelector('input[name="__RequestVerificationToken"]').value;
    let trigger = null;
    let deviceId = null;
    let request = null;
    let generation = 0;

    function clearKey() {
        value.textContent = '';
        valuePanel.hidden = true;
        copyButton.textContent = 'Kopier nøkkel';
    }

    function clear() {
        generation++;
        request?.abort();
        request = null;
        deviceId = null;
        clearKey();
        select.replaceChildren();
        selection.hidden = true;
        error.textContent = '';
        error.hidden = true;
        status.textContent = '';
        deviceName.textContent = '';
        fetchButton.disabled = false;
        select.disabled = false;
    }

    function showError(message) {
        error.textContent = message;
        error.hidden = false;
    }

    async function post(endpoint, fields, signal) {
        const response = await fetch(endpoint, {
            method: 'POST',
            cache: 'no-store',
            signal,
            body: new URLSearchParams({ studentId, deviceId, ...fields, __RequestVerificationToken: token })
        });
        if (!response.ok) throw new Error('Request failed');
        const data = await response.json();
        if (!data.success) {
            data.key = null;
            throw new Error('Request failed');
        }
        return data;
    }

    document.querySelectorAll('.bitlocker-button').forEach(button => {
        button.addEventListener('click', async () => {
            clear();
            trigger = button;
            deviceId = button.dataset.deviceId;
            deviceName.textContent = button.dataset.deviceName || 'Uten enhetsnavn';
            status.textContent = 'Henter tilgjengelige nøkkelmetadata …';
            bootstrap.Tooltip.getInstance(button)?.hide();
            modal.show(button);
            const current = generation;
            request = new AbortController();
            try {
                const data = await post(element.dataset.keysUrl, {}, request.signal);
                if (current !== generation) return;
                if (!Array.isArray(data.keys) || data.keys.length === 0) {
                    status.textContent = 'Ingen BitLocker-nøkler er registrert for enheten.';
                    return;
                }
                data.keys.forEach(key => {
                    const option = document.createElement('option');
                    option.value = key.id;
                    const date = key.createdDateTime ? new Date(key.createdDateTime) : null;
                    const formatted = date && !Number.isNaN(date.getTime())
                        ? new Intl.DateTimeFormat('nb-NO', { dateStyle: 'short', timeStyle: 'short', timeZone: 'Europe/Oslo' }).format(date)
                        : 'Dato ikke tilgjengelig';
                    option.textContent = [key.id, formatted, key.volumeType || 'Ukjent volumtype'].join(' · ');
                    select.append(option);
                });
                status.textContent = data.keys.length + ' nøkkel/nøkler funnet.';
                selection.hidden = false;
            } catch (failure) {
                if (current !== generation || failure.name === 'AbortError') return;
                status.textContent = '';
                showError('Nøkkelmetadata kunne ikke hentes. Kontroller tilgangen eller prøv igjen senere.');
            }
        });
    });

    select.addEventListener('change', clearKey);
    fetchButton.addEventListener('click', async () => {
        if (!deviceId || !select.value || fetchButton.disabled) return;
        clearKey();
        error.hidden = true;
        error.textContent = '';
        fetchButton.disabled = true;
        select.disabled = true;
        status.textContent = 'Henter valgt nøkkel …';
        const current = generation;
        request?.abort();
        request = new AbortController();
        let data = null;
        try {
            data = await post(element.dataset.keyUrl, { keyId: select.value }, request.signal);
            if (current !== generation) return;
            if (typeof data.key !== 'string' || !data.key.trim()) throw new Error('Missing key');
            value.textContent = data.key;
            valuePanel.hidden = false;
            selection.hidden = true;
            status.textContent = 'Nøkkelen er hentet.';
            copyButton.focus();
        } catch (failure) {
            if (current !== generation || failure.name === 'AbortError') return;
            status.textContent = '';
            showError('Nøkkelen kunne ikke hentes. Kontroller tilgangen eller prøv igjen senere.');
        } finally {
            if (data) data.key = null;
            data = null;
            if (current === generation) {
                fetchButton.disabled = false;
                select.disabled = false;
            }
        }
    });

    copyButton.addEventListener('click', async () => {
        if (!value.textContent) return;
        const current = generation;
        try {
            await navigator.clipboard.writeText(value.textContent);
            if (current === generation) copyButton.textContent = 'Kopiert';
        } catch {
            if (current === generation) showError('Kunne ikke kopiere. Marker nøkkelen og kopier manuelt.');
        }
    });
    element.addEventListener('shown.bs.modal', () => element.querySelector('.btn-close').focus());
    element.addEventListener('hide.bs.modal', clear);
    element.addEventListener('hidden.bs.modal', () => {
        trigger?.focus();
        trigger = null;
    });
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape') {
            document.querySelectorAll('.device-actions button').forEach(button => bootstrap.Tooltip.getInstance(button)?.hide());
        }
    });
    window.addEventListener('pagehide', clear);
})();
