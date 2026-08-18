const byId = (id) => document.getElementById(id);
const workspace = byId('workspace');
let inspected = false;
let inspectedData = null;
let titleScreenCatalog = null;
let titleScreenPreviewRequest = 0;

async function post(url, body = {}) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || data.detail || 'Operación fallida');
  return data;
}

function setStatus(message, state = 'neutral') {
  const element = byId('status');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setResult(message, state = 'neutral') {
  const element = byId('result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setExtractResult(message, state = 'neutral') {
  const element = byId('extract-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setRebuildResult(message, state = 'neutral') {
  const element = byId('rebuild-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setCiaResult(message, state = 'neutral') {
  const element = byId('cia-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setPatchResult(message, state = 'neutral') {
  const element = byId('patch-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setGarcUnpackResult(message, state = 'neutral') {
  const element = byId('garc-unpack-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setGarcPackResult(message, state = 'neutral') {
  const element = byId('garc-pack-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setDarcUnpackResult(message, state = 'neutral') {
  const element = byId('darc-unpack-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setDarcPackResult(message, state = 'neutral') {
  const element = byId('darc-pack-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setSarcUnpackResult(message, state = 'neutral') {
  const element = byId('sarc-unpack-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setSarcPackResult(message, state = 'neutral') {
  const element = byId('sarc-pack-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setFarcUnpackResult(message, state = 'neutral') {
  const element = byId('farc-unpack-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setFarcPackResult(message, state = 'neutral') {
  const element = byId('farc-pack-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setTitleScreenResult(message, state = 'neutral') {
  const element = byId('title-screen-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setTitleScreenReplaceResult(message, state = 'neutral') {
  const element = byId('title-screen-replace-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setTitleScreenReplaceGarcResult(message, state = 'neutral') {
  const element = byId('title-screen-replace-garc-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setTitleScreenApplyResult(message, state = 'neutral') {
  const element = byId('title-screen-apply-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setTitleScreenPreviewResult(message, state = 'neutral') {
  const element = byId('title-screen-preview-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function escapeHtml(value) {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

function resetTitleScreenReplacementSelectors() {
  const archiveSelect = byId('title-screen-archive');
  const assetSelect = byId('title-screen-asset');
  archiveSelect.innerHTML = '<option value="">Analizá primero el workspace</option>';
  assetSelect.innerHTML = '<option value="">Analizá primero el archivo de título</option>';
  archiveSelect.disabled = true;
  assetSelect.disabled = true;
  titleScreenPreviewRequest += 1;
  const preview = byId('title-screen-preview');
  const image = byId('title-screen-preview-image');
  preview.hidden = true;
  image.hidden = true;
  image.removeAttribute('src');
  byId('title-screen-preview-name').textContent = 'Vista previa';
  byId('title-screen-preview-meta').textContent = '';
  setTitleScreenPreviewResult('Elegí un recurso BCLIM compatible para verlo.', 'neutral');
  setTitleScreenApplyResult('Elegí un recurso y un archivo de reemplazo para aplicarlo al workspace.', 'neutral');
}

function updateTitleScreenAssets() {
  const archiveSelect = byId('title-screen-archive');
  const assetSelect = byId('title-screen-asset');
  const archive = titleScreenCatalog?.archives.find((entry) =>
    entry.valid && String(entry.fileNumber) === archiveSelect.value);
  const assets = archive?.assets || [];
  assetSelect.innerHTML = assets.length
    ? assets.map((asset) => `<option value="${asset.entryIndex}">${escapeHtml(asset.name)} · ${asset.bytes.toLocaleString()} B</option>`).join('')
    : '<option value="">No hay BCLIM disponibles</option>';
  assetSelect.disabled = assets.length === 0;
  loadTitleScreenPreview();
  updateBuildState();
}

async function loadTitleScreenPreview() {
  const requestId = ++titleScreenPreviewRequest;
  const archiveSelect = byId('title-screen-archive');
  const assetSelect = byId('title-screen-asset');
  const archive = titleScreenCatalog?.archives.find((entry) =>
    entry.valid && String(entry.fileNumber) === archiveSelect.value);
  const asset = archive?.assets.find((entry) => String(entry.entryIndex) === assetSelect.value);
  const preview = byId('title-screen-preview');
  const image = byId('title-screen-preview-image');
  if (!archive || !asset) {
    preview.hidden = true;
    image.hidden = true;
    image.removeAttribute('src');
    return;
  }

  preview.hidden = false;
  image.hidden = true;
  image.removeAttribute('src');
  byId('title-screen-preview-name').textContent = asset.name;
  byId('title-screen-preview-meta').textContent = 'Generando vista previa…';
  setTitleScreenPreviewResult('Leyendo el BCLIM…', 'neutral');
  try {
    const data = await post('/api/editors/titlescreen/preview', {
      workspacePath: workspace.value,
      fileNumber: Number(archiveSelect.value),
      assetEntryIndex: Number(assetSelect.value),
    });
    if (requestId !== titleScreenPreviewRequest) return;
    image.src = `data:image/png;base64,${data.pngBase64}`;
    image.alt = `${data.assetName} · vista previa`;
    image.hidden = false;
    byId('title-screen-preview-meta').textContent = `${data.width}×${data.height} · ${data.bclimFormat}`;
    setTitleScreenPreviewResult('Vista previa disponible; el workspace no fue modificado.', 'success');
  } catch (error) {
    if (requestId !== titleScreenPreviewRequest) return;
    byId('title-screen-preview-meta').textContent = `${asset.bytes.toLocaleString()} B`;
    setTitleScreenPreviewResult(error.message, 'error');
  }
}

function renderTitleScreenCatalog(data) {
  titleScreenCatalog = data;
  const catalog = byId('title-screen-catalog');
  const valid = data.archives.filter((archive) => archive.valid);
  const allValid = valid.length === data.archives.length;
  byId('title-screen-summary').textContent = `${valid.length}/${data.archives.length} archivos legibles · ${data.gameVersion}`;
  byId('export-title-screen').disabled = !allValid;
  const archiveSelect = byId('title-screen-archive');
  archiveSelect.innerHTML = valid.length
    ? valid.map((archive) => `<option value="${archive.fileNumber}">${escapeHtml(archive.game)} · ${escapeHtml(archive.language)} · ${archive.fileNumber}</option>`).join('')
    : '<option value="">No hay archivos de título válidos</option>';
  archiveSelect.disabled = valid.length === 0;
  updateTitleScreenAssets();
  catalog.hidden = false;
  catalog.innerHTML = data.archives.map((archive) => {
    const state = archive.valid ? 'is-valid' : 'is-invalid';
    const details = archive.valid
      ? `${archive.assets.length} BCLIM · ${archive.darcBytes.toLocaleString()} bytes DARC${archive.compressed ? ' · LZSS' : ''}`
      : escapeHtml(archive.error || 'No se pudo leer');
    const assets = archive.valid && archive.assets.length > 0
      ? `<div class="title-screen-assets">${archive.assets.map((asset) => `<span>${escapeHtml(asset.name)} · ${asset.bytes.toLocaleString()} B</span>`).join('')}</div>`
      : '';
    return `<div class="title-screen-row ${state}"><div><b>${escapeHtml(archive.game)} · ${escapeHtml(archive.language)}</b><small>${escapeHtml(archive.romFsPath)} · ${details}</small>${assets}</div><span>${archive.valid ? 'OK' : 'Revisar'}</span></div>`;
  }).join('');
}

function updateBuildState() {
  const hasExeFs = Boolean(inspectedData?.exeFsPath);
  const hasExheader = Boolean(inspectedData?.exheaderPath);
  const exefsControl = byId('exefs');
  exefsControl.disabled = inspected && !hasExeFs;
  if (exefsControl.disabled) exefsControl.checked = false;
  const canBuild = inspected && (byId('romfs').checked || byId('exefs').checked);
  byId('build-action').disabled = !canBuild;
  byId('rebuild-action').disabled = !inspected || !hasExeFs || !hasExheader;
  byId('rebuild-cia-action').disabled = !inspected || !hasExeFs || !hasExheader;
  byId('create-patch').disabled = !inspected || !hasExeFs;
  byId('inspect-title-screen').disabled = !inspected;
  byId('export-title-screen').disabled = !titleScreenCatalog || titleScreenCatalog.archives.some((archive) => !archive.valid);
  const archiveSelect = byId('title-screen-archive');
  const assetSelect = byId('title-screen-asset');
  byId('replace-title-screen').disabled = !titleScreenCatalog
    || archiveSelect.disabled
    || !archiveSelect.value
    || assetSelect.disabled
    || !assetSelect.value;
  byId('replace-title-screen-garc').disabled = byId('replace-title-screen').disabled;
  byId('apply-title-screen').disabled = byId('replace-title-screen').disabled;
  if (!byId('romfs').checked && !byId('exefs').checked)
    byId('summary').textContent = 'Elegí al menos un archivo';
  else if (inspected)
    byId('summary').textContent = 'Listo para construir';
  else
    byId('summary').textContent = 'Cargá un workspace para continuar';
}

async function inspectWorkspace() {
  const button = byId('inspect-action');
  button.disabled = true;
  inspected = false;
  inspectedData = null;
  titleScreenCatalog = null;
  byId('title-screen-catalog').hidden = true;
  byId('title-screen-summary').textContent = 'Cargá un workspace para analizarlo.';
  resetTitleScreenReplacementSelectors();
  setTitleScreenReplaceResult('Analizá el workspace para elegir una imagen.', 'neutral');
  setTitleScreenApplyResult('Analizá el workspace para elegir una imagen.', 'neutral');
  setStatus('Cargando el juego…');
  try {
    const data = await post('/api/workspace/inspect', { workspacePath: workspace.value });
    inspected = true;
    inspectedData = data;
    setCiaResult('', 'neutral');
    const exefs = data.exeFsPath ? ' ExeFS detectado.' : ' No hay ExeFS detectado.';
    setStatus(`Listo: ${data.gameVersion}.${exefs}`, 'success');
    setResult('Podés construir una copia binaria desde este workspace.');
  } catch (error) {
    setStatus(error.message, 'error');
    setResult('Cargá un workspace válido para continuar.', 'neutral');
  } finally {
    button.disabled = false;
    updateBuildState();
  }
}

byId('browse').addEventListener('click', async () => {
  const button = byId('browse');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick');
    workspace.value = data.path;
    await inspectWorkspace();
  } catch (error) {
    setStatus(error.message);
  } finally {
    button.disabled = false;
  }
});

byId('browse-output').addEventListener('click', async () => {
  const button = byId('browse-output');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('output').value = data.path;
  } catch (error) {
    setResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-file').addEventListener('click', async () => {
  const button = byId('browse-file');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-file');
    byId('input-file').value = data.path;
  } catch (error) {
    setExtractResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-extract-output').addEventListener('click', async () => {
  const button = byId('browse-extract-output');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('extract-output').value = data.path;
  } catch (error) {
    setExtractResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-makerom').addEventListener('click', async () => {
  const button = byId('browse-makerom');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-tool');
    byId('makerom-path').value = data.path;
  } catch (error) {
    setCiaResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-patch-output').addEventListener('click', async () => {
  const button = byId('browse-patch-output');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('patch-output').value = data.path;
  } catch (error) {
    setPatchResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-garc-input').addEventListener('click', async () => {
  const button = byId('browse-garc-input');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-archive');
    byId('garc-input').value = data.path;
  } catch (error) {
    setGarcUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-garc-unpack-output').addEventListener('click', async () => {
  const button = byId('browse-garc-unpack-output');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('garc-unpack-output').value = data.path;
  } catch (error) {
    setGarcUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-garc-folder').addEventListener('click', async () => {
  const button = byId('browse-garc-folder');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('garc-folder').value = data.path;
  } catch (error) {
    setGarcPackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-darc-input').addEventListener('click', async () => {
  const button = byId('browse-darc-input');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-archive');
    byId('darc-input').value = data.path;
  } catch (error) {
    setDarcUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-darc-unpack-output').addEventListener('click', async () => {
  const button = byId('browse-darc-unpack-output');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('darc-unpack-output').value = data.path;
  } catch (error) {
    setDarcUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-darc-folder').addEventListener('click', async () => {
  const button = byId('browse-darc-folder');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('darc-folder').value = data.path;
  } catch (error) {
    setDarcPackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-sarc-input').addEventListener('click', async () => {
  const button = byId('browse-sarc-input');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-archive');
    byId('sarc-input').value = data.path;
  } catch (error) {
    setSarcUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-sarc-unpack-output').addEventListener('click', async () => {
  const button = byId('browse-sarc-unpack-output');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('sarc-unpack-output').value = data.path;
  } catch (error) {
    setSarcUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-sarc-folder').addEventListener('click', async () => {
  const button = byId('browse-sarc-folder');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('sarc-folder').value = data.path;
  } catch (error) {
    setSarcPackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-farc-input').addEventListener('click', async () => {
  const button = byId('browse-farc-input');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-archive');
    byId('farc-input').value = data.path;
  } catch (error) {
    setFarcUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-farc-unpack-output').addEventListener('click', async () => {
  const button = byId('browse-farc-unpack-output');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('farc-unpack-output').value = data.path;
  } catch (error) {
    setFarcUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-farc-folder').addEventListener('click', async () => {
  const button = byId('browse-farc-folder');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('farc-folder').value = data.path;
  } catch (error) {
    setFarcPackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('inspect-action').addEventListener('click', inspectWorkspace);
workspace.addEventListener('input', () => {
  inspected = false;
  titleScreenCatalog = null;
  byId('title-screen-catalog').hidden = true;
  byId('title-screen-summary').textContent = 'Cargá un workspace para analizarlo.';
  resetTitleScreenReplacementSelectors();
  setTitleScreenReplaceResult('Analizá el workspace para elegir una imagen.', 'neutral');
  setStatus('La ruta cambió. Cargala otra vez antes de construir.');
  updateBuildState();
});
document.querySelectorAll('#romfs, #exefs').forEach((control) => control.addEventListener('change', updateBuildState));

byId('build-action').addEventListener('click', async () => {
  const button = byId('build-action');
  button.disabled = true;
  setResult('Construyendo binarios…');
  try {
    const data = await post('/api/workspace/build-filesystems', {
      workspacePath: workspace.value,
      outputDirectory: byId('output').value.trim() || null,
      includeRomFs: byId('romfs').checked,
      includeExeFs: byId('exefs').checked,
    });
    const files = [data.romFsFile, data.exeFsFile].filter(Boolean);
    setResult(`Listo. Se generaron ${files.length} archivo(s) en ${data.outputDirectory}.`, 'success');
  } catch (error) {
    setResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('extract-action').addEventListener('click', async () => {
  const button = byId('extract-action');
  button.disabled = true;
  setExtractResult('Extrayendo el archivo…');
  try {
    const data = await post('/api/workspace/extract', {
      inputPath: byId('input-file').value,
      outputDirectory: byId('extract-output').value.trim() || null,
    });
    setExtractResult(`Listo: ${data.format}. Se generaron ${data.files.length} archivos en ${data.outputDirectory}.`, 'success');
  } catch (error) {
    setExtractResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('rebuild-action').addEventListener('click', async () => {
  const button = byId('rebuild-action');
  button.disabled = true;
  setRebuildResult('Empaquetando RomFS y ExeFS y ensamblando la ROM…');
  try {
    const data = await post('/api/workspace/rebuild-rom', {
      workspacePath: workspace.value,
      outputFile: byId('rom-output').value.trim() || null,
      trimmed: byId('trimmed').checked,
    });
    setRebuildResult(`Listo: ${data.outputFile} (${data.bytes.toLocaleString()} bytes).`, 'success');
  } catch (error) {
    setRebuildResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('rebuild-cia-action').addEventListener('click', async () => {
  const button = byId('rebuild-cia-action');
  button.disabled = true;
  setCiaResult('Reconstruyendo la ROM intermedia y creando el CIA…');
  try {
    const data = await post('/api/workspace/rebuild-cia', {
      workspacePath: workspace.value,
      outputFile: byId('cia-output').value.trim() || null,
      trimmed: byId('cia-trimmed').checked,
      makeromPath: byId('makerom-path').value.trim() || null,
    });
    setCiaResult(`Listo: ${data.outputFile} (${data.bytes.toLocaleString()} bytes).`, 'success');
  } catch (error) {
    setCiaResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('create-patch').addEventListener('click', async () => {
  const button = byId('create-patch');
  button.disabled = true;
  setPatchResult('Creando el parche de redirección…');
  try {
    const names = byId('patch-garcs').value
      .split(/[\n,]/)
      .map((value) => value.trim())
      .filter(Boolean);
    const data = await post('/api/workspace/redirect-patch', {
      workspacePath: workspace.value,
      garcNames: names,
      outputDirectory: byId('patch-output').value.trim() || null,
      includeAllLanguageVariants: byId('patch-all-languages').checked,
    });
    setPatchResult(`Listo: ${data.redirectedPaths} ruta(s) redirigida(s) en ${data.outputDirectory}.`, 'success');
  } catch (error) {
    setPatchResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('unpack-garc').addEventListener('click', async () => {
  const button = byId('unpack-garc');
  button.disabled = true;
  setGarcUnpackResult('Desempaquetando el GARC…');
  try {
    const data = await post('/api/workspace/unpack-garc', {
      inputFile: byId('garc-input').value,
      outputDirectory: byId('garc-unpack-output').value.trim() || null,
      skipDecompression: byId('garc-skip-decompression').checked,
    });
    setGarcUnpackResult(`Listo: ${data.files} archivo(s) en ${data.outputDirectory}.`, 'success');
  } catch (error) {
    setGarcUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('pack-garc').addEventListener('click', async () => {
  const button = byId('pack-garc');
  button.disabled = true;
  setGarcPackResult('Empaquetando el GARC…');
  try {
    const data = await post('/api/workspace/pack-garc', {
      inputDirectory: byId('garc-folder').value,
      outputFile: byId('garc-pack-output').value.trim() || null,
      version: Number(byId('garc-version').value),
      bytesPadding: Number(byId('garc-padding').value),
    });
    setGarcPackResult(`Listo: ${data.files} archivo(s), ${data.bytes.toLocaleString()} bytes.`, 'success');
  } catch (error) {
    setGarcPackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('unpack-darc').addEventListener('click', async () => {
  const button = byId('unpack-darc');
  button.disabled = true;
  setDarcUnpackResult('Desempaquetando el DARC…');
  try {
    const data = await post('/api/workspace/unpack-darc', {
      inputFile: byId('darc-input').value,
      outputDirectory: byId('darc-unpack-output').value.trim() || null,
    });
    setDarcUnpackResult(`Listo: ${data.files} archivo(s) en ${data.outputDirectory}.`, 'success');
  } catch (error) {
    setDarcUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('pack-darc').addEventListener('click', async () => {
  const button = byId('pack-darc');
  button.disabled = true;
  setDarcPackResult('Empaquetando el DARC…');
  try {
    const data = await post('/api/workspace/pack-darc', {
      inputDirectory: byId('darc-folder').value,
      outputFile: byId('darc-pack-output').value.trim() || null,
    });
    setDarcPackResult(`Listo: ${data.files} archivo(s), ${data.bytes.toLocaleString()} bytes.`, 'success');
  } catch (error) {
    setDarcPackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('unpack-sarc').addEventListener('click', async () => {
  const button = byId('unpack-sarc');
  button.disabled = true;
  setSarcUnpackResult('Desempaquetando el SARC…');
  try {
    const data = await post('/api/workspace/unpack-sarc', {
      inputFile: byId('sarc-input').value,
      outputDirectory: byId('sarc-unpack-output').value.trim() || null,
    });
    setSarcUnpackResult(`Listo: ${data.files} archivo(s) en ${data.outputDirectory}.`, 'success');
  } catch (error) {
    setSarcUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('pack-sarc').addEventListener('click', async () => {
  const button = byId('pack-sarc');
  button.disabled = true;
  setSarcPackResult('Empaquetando el SARC…');
  try {
    const data = await post('/api/workspace/pack-sarc', {
      inputDirectory: byId('sarc-folder').value,
      outputFile: byId('sarc-pack-output').value.trim() || null,
      dataAlignment: Number(byId('sarc-alignment').value),
    });
    setSarcPackResult(`Listo: ${data.files} archivo(s), ${data.bytes.toLocaleString()} bytes.`, 'success');
  } catch (error) {
    setSarcPackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('unpack-farc').addEventListener('click', async () => {
  const button = byId('unpack-farc');
  button.disabled = true;
  setFarcUnpackResult('Desempaquetando el FARC…');
  try {
    const data = await post('/api/workspace/unpack-farc', {
      inputFile: byId('farc-input').value,
      outputDirectory: byId('farc-unpack-output').value.trim() || null,
    });
    setFarcUnpackResult(`Listo: ${data.files} archivo(s) en ${data.outputDirectory}.`, 'success');
  } catch (error) {
    setFarcUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('pack-farc').addEventListener('click', async () => {
  const button = byId('pack-farc');
  button.disabled = true;
  setFarcPackResult('Empaquetando el FARC…');
  try {
    const data = await post('/api/workspace/pack-farc', {
      inputDirectory: byId('farc-folder').value,
      outputFile: byId('farc-pack-output').value.trim() || null,
      dataAlignment: Number(byId('farc-alignment').value),
    });
    setFarcPackResult(`Listo: ${data.files} archivo(s), ${data.bytes.toLocaleString()} bytes.`, 'success');
  } catch (error) {
    setFarcPackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-title-screen-output').addEventListener('click', async () => {
  const button = byId('browse-title-screen-output');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('title-screen-output').value = data.path;
  } catch (error) {
    setTitleScreenResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-title-screen-replacement').addEventListener('click', async () => {
  const button = byId('browse-title-screen-replacement');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-image');
    byId('title-screen-replacement').value = data.path;
  } catch (error) {
    setTitleScreenReplaceResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('title-screen-archive').addEventListener('change', updateTitleScreenAssets);
byId('title-screen-asset').addEventListener('change', loadTitleScreenPreview);

byId('inspect-title-screen').addEventListener('click', async () => {
  const button = byId('inspect-title-screen');
  button.disabled = true;
  setTitleScreenResult('Leyendo los DARCs de pantalla de título…');
  try {
    const data = await post('/api/editors/titlescreen/catalog', { workspacePath: workspace.value });
    renderTitleScreenCatalog(data);
    const invalid = data.archives.length - data.archives.filter((archive) => archive.valid).length;
    setTitleScreenResult(invalid === 0
      ? `Listo: ${data.archives.length} archivos y sus BCLIM están disponibles para exportar.`
      : `Inventario listo: ${invalid} archivo(s) no se pudo/pudieron leer.`, invalid === 0 ? 'success' : 'neutral');
  } catch (error) {
    titleScreenCatalog = null;
    byId('title-screen-catalog').hidden = true;
    resetTitleScreenReplacementSelectors();
    setTitleScreenResult(error.message, 'error');
    setTitleScreenApplyResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('export-title-screen').addEventListener('click', async () => {
  const button = byId('export-title-screen');
  button.disabled = true;
  setTitleScreenResult('Exportando DARCs, BCLIM, PNG compatibles y manifest.json…');
  try {
    const data = await post('/api/editors/titlescreen/export', {
      workspacePath: workspace.value,
      outputDirectory: byId('title-screen-output').value.trim() || null,
      includeRawDarc: true,
      includePng: true,
    });
    setTitleScreenResult(`Listo: ${data.archives} archivo(s), ${data.assets} BCLIM y ${data.pngs} PNG en ${data.outputDirectory}.`, 'success');
  } catch (error) {
    setTitleScreenResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('replace-title-screen').addEventListener('click', async () => {
  const button = byId('replace-title-screen');
  button.disabled = true;
  setTitleScreenReplaceResult('Generando un DARC con la imagen reemplazada…');
  try {
    const data = await post('/api/editors/titlescreen/replace', {
      workspacePath: workspace.value,
      fileNumber: Number(byId('title-screen-archive').value),
      assetEntryIndex: Number(byId('title-screen-asset').value),
      replacementFile: byId('title-screen-replacement').value,
      outputFile: byId('title-screen-replace-output').value.trim() || null,
    });
    setTitleScreenReplaceResult(`Listo: ${data.replacementFormat} convertido a ${data.bclimFormat}. DARC generado en ${data.outputFile}.`, 'success');
  } catch (error) {
    setTitleScreenReplaceResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('replace-title-screen-garc').addEventListener('click', async () => {
  const button = byId('replace-title-screen-garc');
  button.disabled = true;
  setTitleScreenReplaceGarcResult('Generando una copia completa del GARC…');
  try {
    const data = await post('/api/editors/titlescreen/replace-garc', {
      workspacePath: workspace.value,
      fileNumber: Number(byId('title-screen-archive').value),
      assetEntryIndex: Number(byId('title-screen-asset').value),
      replacementFile: byId('title-screen-replacement').value,
      outputFile: byId('title-screen-replace-garc-output').value.trim() || null,
    });
    const compression = data.compressed ? ' comprimido con LZSS' : '';
    setTitleScreenReplaceGarcResult(`Listo: GARC${compression} generado en ${data.outputFile}.`, 'success');
  } catch (error) {
    setTitleScreenReplaceGarcResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('apply-title-screen').addEventListener('click', async () => {
  const button = byId('apply-title-screen');
  button.disabled = true;
  setTitleScreenApplyResult('Guardando una copia y actualizando el GARC del workspace…');
  try {
    const data = await post('/api/editors/titlescreen/apply', {
      workspacePath: workspace.value,
      fileNumber: Number(byId('title-screen-archive').value),
      assetEntryIndex: Number(byId('title-screen-asset').value),
      replacementFile: byId('title-screen-replacement').value,
    });
    setTitleScreenApplyResult(`Listo: workspace actualizado. Copia de seguridad en ${data.backupFile}.`, 'success');
    loadTitleScreenPreview();
  } catch (error) {
    setTitleScreenApplyResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

updateBuildState();
