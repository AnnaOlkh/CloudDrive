// Допоміжні функції для Blazor-клієнта CloudDrive.
// Усе, що Blazor не вміє напряму: localStorage, збереження файлу, drag-and-drop з ОС, File System Access API.
window.cloudDrive = (() => {
    // ---------------------------------------------------------------- localStorage
    const storage = {
        get: key => { try { return localStorage.getItem(key); } catch { return null; } },
        set: (key, value) => { try { localStorage.setItem(key, value); } catch { /* квота або приватний режим */ } },
        remove: key => { try { localStorage.removeItem(key); } catch { } }
    };

    // ---------------------------------------------------------------- DOM helpers
    const click = id => document.getElementById(id)?.click();
    const resetInput = id => { const el = document.getElementById(id); if (el) el.value = ''; };

    // ---------------------------------------------------------------- збереження файлу
    async function saveFile(fileName, streamReference) {
        const buffer = await streamReference.arrayBuffer();
        const url = URL.createObjectURL(new Blob([buffer]));
        const link = document.createElement('a');
        link.href = url;
        link.download = fileName;
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 10000);
    }

    // ---------------------------------------------------------------- drag-and-drop файлів з ОС
    // Файли кладемо в той самий прихований <InputFile id="upload-input">, що й кнопка Upload,
    // і викликаємо його "change": так Blazor отримує їх як звичайний вибір файлів.
    let dropTarget = null;
    let dragDepth = 0;

    const hasFiles = e => e.dataTransfer && Array.from(e.dataTransfer.types || []).includes('Files');
    const setDragging = on => document.body.classList.toggle('dragging', on);

    const onDragEnter = e => {
        if (!hasFiles(e)) return;
        e.preventDefault();
        dragDepth++;
        setDragging(true);
    };
    const onDragOver = e => {
        if (!hasFiles(e)) return;
        e.preventDefault();
        e.dataTransfer.dropEffect = 'copy';
    };
    const onDragLeave = e => {
        if (!hasFiles(e)) return;
        dragDepth = Math.max(0, dragDepth - 1);
        if (dragDepth === 0) setDragging(false);
    };
    const onDrop = e => {
        if (!hasFiles(e)) return;
        e.preventDefault(); // інакше браузер відкриє файл замість завантаження
        dragDepth = 0;
        setDragging(false);

        const input = document.getElementById('upload-input');
        if (!input || !dropTarget) return;

        // DataTransfer.items дійсні лише синхронно в обробнику події.
        const files = [];
        let directories = 0;
        for (const item of Array.from(e.dataTransfer.items || [])) {
            if (item.kind !== 'file') continue;
            const entry = item.webkitGetAsEntry ? item.webkitGetAsEntry() : null;
            if (entry && entry.isDirectory) { directories++; continue; }
            const file = item.getAsFile();
            if (file) files.push(file);
        }

        if (directories > 0) dropTarget.invokeMethodAsync('OnDirectoriesDropped', directories);
        if (files.length === 0) return;

        const transfer = new DataTransfer();
        files.forEach(f => transfer.items.add(f));
        input.files = transfer.files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    };

    function registerDrop(dotNetReference) {
        unregisterDrop();
        dropTarget = dotNetReference;
        window.addEventListener('dragenter', onDragEnter);
        window.addEventListener('dragover', onDragOver);
        window.addEventListener('dragleave', onDragLeave);
        window.addEventListener('drop', onDrop);
    }

    function unregisterDrop() {
        window.removeEventListener('dragenter', onDragEnter);
        window.removeEventListener('dragover', onDragOver);
        window.removeEventListener('dragleave', onDragLeave);
        window.removeEventListener('drop', onDrop);
        dropTarget = null;
        dragDepth = 0;
        setDragging(false);
    }

    // ---------------------------------------------------------------- File System Access API (Chromium)
    const handles = new Map();
    let sequence = 0;

    async function sha256Hex(buffer) {
        const digest = await crypto.subtle.digest('SHA-256', buffer);
        return Array.from(new Uint8Array(digest)).map(b => b.toString(16).padStart(2, '0')).join('');
    }

    function root(id) {
        const handle = handles.get(id);
        if (!handle) throw new Error('The selected folder is no longer available. Choose it again.');
        return handle;
    }

    async function directoryFor(rootHandle, parts, create) {
        let dir = rootHandle;
        for (const part of parts) dir = await dir.getDirectoryHandle(part, { create });
        return dir;
    }

    async function fileHandle(id, path, create) {
        const parts = path.split('/');
        const name = parts.pop();
        const dir = await directoryFor(root(id), parts, create);
        return dir.getFileHandle(name, { create });
    }

    async function* walk(dir, prefix) {
        for await (const [name, handle] of dir.entries()) {
            if (handle.kind === 'directory') yield* walk(handle, prefix + name + '/');
            else yield { path: prefix + name, handle };
        }
    }

    const isMissing = e => e && (e.name === 'NotFoundError' || e.name === 'TypeMismatchError');

    const fs = {
        supported: () => typeof window.showDirectoryPicker === 'function' && window.isSecureContext,

        pick: async () => {
            try {
                const handle = await window.showDirectoryPicker({ mode: 'readwrite' });
                const id = ++sequence;
                handles.set(id, handle);
                return { id, name: handle.name };
            } catch (e) {
                if (e && e.name === 'AbortError') return null; // користувач закрив вікно вибору
                throw e;
            }
        },

        scan: async id => {
            const result = [];
            for await (const { path, handle } of walk(root(id), '')) {
                const file = await handle.getFile();
                result.push({ path, hash: await sha256Hex(await file.arrayBuffer()), size: file.size });
            }
            return result;
        },

        read: async (id, path) => {
            const file = await (await fileHandle(id, path, false)).getFile();
            return new Uint8Array(await file.arrayBuffer());
        },

        write: async (id, path, data) => {
            const handle = await fileHandle(id, path, true);
            const writable = await handle.createWritable(); // запис підтверджується лише при close()
            await writable.write(data);
            await writable.close();
        },

        hash: async (id, path) => {
            try {
                const file = await (await fileHandle(id, path, false)).getFile();
                return await sha256Hex(await file.arrayBuffer());
            } catch (e) {
                if (isMissing(e)) return null;
                throw e;
            }
        },

        exists: async (id, path) => {
            try { await fileHandle(id, path, false); return true; }
            catch (e) { if (isMissing(e)) return false; throw e; }
        },

        remove: async (id, path) => {
            const parts = path.split('/');
            const name = parts.pop();
            const dir = await directoryFor(root(id), parts, false);
            await dir.removeEntry(name);
        },

        release: id => { handles.delete(id); }
    };

    return { storage, click, resetInput, saveFile, registerDrop, unregisterDrop, fs };
})();
