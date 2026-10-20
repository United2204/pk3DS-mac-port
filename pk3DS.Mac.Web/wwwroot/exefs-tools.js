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

  byId('browse-exefs-unpack-input').addEventListener('click', () =>
    choose('browse-exefs-unpack-input', '/api/workspace/pick-exefs-file', 'exefs-unpack-input', 'exefs-unpack-result'));
  byId('browse-exefs-unpack-output').addEventListener('click', () =>
    choose('browse-exefs-unpack-output', '/api/workspace/pick-output', 'exefs-unpack-output', 'exefs-unpack-result'));
  byId('browse-exefs-pack-input').addEventListener('click', () =>
    choose('browse-exefs-pack-input', '/api/workspace/pick-exefs-folder', 'exefs-pack-input', 'exefs-pack-result'));

  byId('unpack-exefs-action').addEventListener('click', async () => {
    const button = byId('unpack-exefs-action');
    button.disabled = true;
    show('exefs-unpack-result', 'Validando y desempaquetando ExeFS…');
    try {
      const data = await post('/api/workspace/unpack-exefs', {
        inputFile: byId('exefs-unpack-input').value.trim(),
        outputDirectory: byId('exefs-unpack-output').value.trim() || null,
      });
      show('exefs-unpack-result', `Listo: ${data.entryCount} archivos en ${data.outputDirectory}.`, 'success');
    } catch (error) {
      show('exefs-unpack-result', error.message, 'error');
    } finally {
      button.disabled = false;
    }
  });

  byId('pack-exefs-action').addEventListener('click', async () => {
    const button = byId('pack-exefs-action');
    button.disabled = true;
    show('exefs-pack-result', 'Validando y empaquetando ExeFS…');
    try {
      const data = await post('/api/workspace/pack-exefs', {
        inputDirectory: byId('exefs-pack-input').value.trim(),
        outputFile: byId('exefs-pack-output').value.trim() || null,
      });
      show('exefs-pack-result', `Listo: ${data.entryCount} archivos, ${Number(data.outputBytes).toLocaleString()} B. Salida: ${data.outputFile}.`, 'success');
    } catch (error) {
      show('exefs-pack-result', error.message, 'error');
    } finally {
      button.disabled = false;
    }
  });
})();
