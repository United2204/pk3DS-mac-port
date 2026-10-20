(() => {
  const byId = (id) => document.getElementById(id);

  async function post(url, body = {}) {
    const response = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });
    const raw = await response.text();
    let data = {};
    if (raw.trim()) {
      try {
        data = JSON.parse(raw);
      } catch {
        data = { error: raw.trim() };
      }
    }
    if (!response.ok) {
      throw new Error(data.error || data.detail || data.title || `El servidor rechazó la operación (${response.status}).`);
    }
    return data;
  }

  function show(id, message, state = 'neutral') {
    const element = byId(id);
    element.textContent = message;
    element.className = `status ${state}`;
  }

  async function choose(buttonId, route, fieldId, resultId) {
    const button = byId(buttonId);
    button.disabled = true;
    try {
      const data = await post(route);
      byId(fieldId).value = data.path;
    } catch (error) {
      show(resultId, error.message, 'error');
    } finally {
      button.disabled = false;
    }
  }

  byId('browse-romfs-unpack-input').addEventListener('click', () =>
    choose('browse-romfs-unpack-input', '/api/workspace/pick-romfs-file', 'romfs-unpack-input', 'romfs-unpack-result'));
  byId('browse-romfs-unpack-output').addEventListener('click', () =>
    choose('browse-romfs-unpack-output', '/api/workspace/pick-output', 'romfs-unpack-output', 'romfs-unpack-result'));
  byId('browse-romfs-pack-input').addEventListener('click', () =>
    choose('browse-romfs-pack-input', '/api/workspace/pick-romfs-folder', 'romfs-pack-input', 'romfs-pack-result'));

  byId('unpack-romfs-action').addEventListener('click', async () => {
    const button = byId('unpack-romfs-action');
    button.disabled = true;
    show('romfs-unpack-result', 'Validando y desempaquetando RomFS…');
    try {
      const data = await post('/api/workspace/unpack-romfs', {
        inputFile: byId('romfs-unpack-input').value.trim(),
        outputDirectory: byId('romfs-unpack-output').value.trim() || null,
      });
      show('romfs-unpack-result', `Listo: ${data.fileCount.toLocaleString()} archivos y ${data.directoryCount.toLocaleString()} carpetas, ${Number(data.fileBytes).toLocaleString()} B. Salida: ${data.outputDirectory}.`, 'success');
    } catch (error) {
      show('romfs-unpack-result', error.message, 'error');
    } finally {
      button.disabled = false;
    }
  });

  byId('pack-romfs-action').addEventListener('click', async () => {
    const button = byId('pack-romfs-action');
    button.disabled = true;
    show('romfs-pack-result', 'Revisando y empaquetando RomFS…');
    try {
      const data = await post('/api/workspace/pack-romfs', {
        inputDirectory: byId('romfs-pack-input').value.trim(),
        outputFile: byId('romfs-pack-output').value.trim() || null,
      });
      show('romfs-pack-result', `Listo: ${data.fileCount.toLocaleString()} archivos y ${data.directoryCount.toLocaleString()} carpetas, ${Number(data.outputBytes).toLocaleString()} B. Salida: ${data.outputFile}.`, 'success');
    } catch (error) {
      show('romfs-pack-result', error.message, 'error');
    } finally {
      button.disabled = false;
    }
  });
})();
