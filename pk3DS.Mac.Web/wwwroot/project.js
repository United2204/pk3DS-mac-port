const byId = (id) => document.getElementById(id);
const workspace = byId('workspace');
let inspected = false;
let inspectedData = null;
let smdhData = null;
let smdhBackups = null;
let smdhSlot = 0;
let titleScreenCatalog = null;
let titleScreenBackups = null;
let titleScreenPreviewRequest = 0;

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
  if (!raw.trim()) throw new Error('El servidor no devolvió una respuesta válida. Reiniciá la aplicación y volvé a intentar.');
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

function setCrrResult(message, state = 'neutral') {
  const element = byId('crr-result');
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

function setAutoUnpackResult(message, state = 'neutral') {
  const element = byId('auto-unpack-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setAutoPackResult(message, state = 'neutral') {
  const element = byId('auto-pack-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setGarcPackResult(message, state = 'neutral') {
  const element = byId('garc-pack-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setGarcShuffleResult(message, state = 'neutral') {
  const element = byId('garc-shuffle-result');
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

function setMiniUnpackResult(message, state = 'neutral') {
  const element = byId('mini-unpack-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setMiniPackResult(message, state = 'neutral') {
  const element = byId('mini-pack-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setAlytResult(message, state = 'neutral') {
  const element = byId('alyt-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setAlytPackResult(message, state = 'neutral') {
  const element = byId('alyt-pack-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setShuffleArcResult(message, state = 'neutral') {
  const element = byId('shuffle-arc-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setGarResult(message, state = 'neutral') {
  const element = byId('gar-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setImageResult(message, state = 'neutral') {
  const element = byId('image-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setSmdhResult(message, state = 'neutral') {
  const element = byId('smdh-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setSmdhUpdateResult(message, state = 'neutral') {
  const element = byId('smdh-update-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setSmdhImportResult(message, state = 'neutral') {
  const element = byId('smdh-import-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setSmdhRestoreResult(message, state = 'neutral') {
  const element = byId('smdh-restore-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setLz11Result(message, state = 'neutral') {
  const element = byId('lz11-result');
  element.textContent = message;
  element.className = `status ${state}`;
}

function setBlzResult(message, state = 'neutral') {
  const element = byId('blz-result');
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

function setTitleScreenRestoreResult(message, state = 'neutral') {
  const element = byId('title-screen-restore-result');
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

function parseSmdhHex(value, label, maximum) {
  let normalized = String(value ?? '').trim();
  if (normalized.toLowerCase().startsWith('0x')) normalized = normalized.slice(2);
  if (!normalized || !/^[0-9a-f]+$/i.test(normalized)) {
    throw new Error(`${label} debe ser un valor hexadecimal.`);
  }
  const parsed = BigInt(`0x${normalized}`);
  if (parsed < 0n || parsed > maximum) {
    throw new Error(`${label} está fuera de rango.`);
  }
  return parsed;
}

function formatSmdhHex(value, width) {
  return `0x${BigInt(value ?? 0).toString(16).toUpperCase().padStart(width, '0')}`;
}

function parseSmdhRatings(value) {
  const normalized = String(value ?? '').replace(/\s+/g, '');
  if (!/^[0-9a-f]{32}$/i.test(normalized)) {
    throw new Error('Ratings regionales debe contener exactamente 16 bytes hexadecimales.');
  }
  return Array.from({ length: 16 }, (_, index) => Number.parseInt(normalized.slice(index * 2, index * 2 + 2), 16));
}

function formatSmdhRatings(values) {
  return Array.from(values || [], (value) => Number(value || 0).toString(16).padStart(2, '0')).join(' ').toUpperCase();
}

function setSmdhCheckboxes(selector, value) {
  const numericValue = Number(value || 0);
  for (const input of document.querySelectorAll(selector)) {
    const bit = Number(input.dataset.smdhRegion || input.dataset.smdhFlag || 0);
    input.checked = (numericValue & bit) !== 0;
  }
}

function readSmdhCheckboxes(selector, preservedValue = 0) {
  const inputs = Array.from(document.querySelectorAll(selector));
  const knownMask = inputs.reduce((value, input) => value | Number(input.dataset.smdhRegion || input.dataset.smdhFlag || 0), 0);
  const selected = inputs.reduce((value, input) => (
    input.checked ? value | Number(input.dataset.smdhRegion || input.dataset.smdhFlag || 0) : value
  ), 0);
  return (((Number(preservedValue || 0) >>> 0) & (~knownMask >>> 0)) | selected) >>> 0;
}

function resetSmdhSettings() {
  byId('smdh-ratings').value = formatSmdhRatings(new Array(16).fill(0));
  setSmdhCheckboxes('[data-smdh-region]', 0);
  setSmdhCheckboxes('[data-smdh-flag]', 0);
  byId('smdh-matchmaker').value = formatSmdhHex(0, 8);
  byId('smdh-matchmaker-bit').value = formatSmdhHex(0, 16);
  byId('smdh-eula').value = '0';
  byId('smdh-animation').value = '0';
  byId('smdh-streetpass').value = formatSmdhHex(0, 8);
}

function loadSmdhSettings(settings = {}) {
  byId('smdh-ratings').value = formatSmdhRatings(settings.gameRatings || new Array(16).fill(0));
  setSmdhCheckboxes('[data-smdh-region]', settings.regionLockout);
  setSmdhCheckboxes('[data-smdh-flag]', settings.flags);
  byId('smdh-matchmaker').value = formatSmdhHex(settings.matchMakerId, 8);
  byId('smdh-matchmaker-bit').value = String(settings.matchMakerBitId || formatSmdhHex(0, 16)).toUpperCase();
  byId('smdh-eula').value = String(Number(settings.eulaVersion || 0));
  byId('smdh-animation').value = String(Number(settings.animationDefaultFrame || 0));
  byId('smdh-streetpass').value = formatSmdhHex(settings.streetPassId, 8);
}

function captureSmdhSettings() {
  const existing = smdhData?.settings || {};
  const matchMakerId = parseSmdhHex(byId('smdh-matchmaker').value, 'MatchMaker ID', 0xFFFFFFFFn);
  const matchMakerBitId = parseSmdhHex(byId('smdh-matchmaker-bit').value, 'MatchMaker BIT ID', 0xFFFFFFFFFFFFFFFFn);
  const streetPassId = parseSmdhHex(byId('smdh-streetpass').value, 'StreetPass ID', 0xFFFFFFFFn);
  const eulaVersion = Number.parseInt(byId('smdh-eula').value, 10);
  const animationDefaultFrame = Number.parseFloat(byId('smdh-animation').value);
  if (!Number.isInteger(eulaVersion) || eulaVersion < 0 || eulaVersion > 0xFFFF) {
    throw new Error('La versión EULA debe ser un entero entre 0 y 65535.');
  }
  if (!Number.isFinite(animationDefaultFrame) || Math.abs(animationDefaultFrame) > 1_000_000) {
    throw new Error('El frame de animación debe ser un número finito válido.');
  }
  return {
    gameRatings: parseSmdhRatings(byId('smdh-ratings').value),
    regionLockout: readSmdhCheckboxes('[data-smdh-region]', existing.regionLockout),
    matchMakerId: Number(matchMakerId),
    matchMakerBitId: formatSmdhHex(matchMakerBitId, 16),
    flags: readSmdhCheckboxes('[data-smdh-flag]', existing.flags),
    eulaVersion,
    reserved: Number(existing.reserved || 0),
    animationDefaultFrame,
    streetPassId: Number(streetPassId),
  };
}

function resetSmdh() {
  smdhData = null;
  smdhBackups = null;
  smdhSlot = 0;
  byId('smdh-summary').textContent = 'Cargá un workspace para analizarlo.';
  byId('smdh-preview').hidden = true;
  byId('smdh-editing').hidden = true;
  byId('smdh-app-info').hidden = true;
  byId('smdh-app-info').innerHTML = '';
  for (const id of ['smdh-small-icon', 'smdh-large-icon']) {
    const image = byId(id);
    image.hidden = true;
    image.removeAttribute('src');
  }
  resetSmdhSettings();
  setSmdhResult('Analizá el workspace para leer icon.bin.', 'neutral');
  setSmdhUpdateResult('Analizá el workspace para editar icon.bin.', 'neutral');
  setSmdhImportResult('Analizá el workspace para importar un icon.bin.', 'neutral');
  byId('smdh-backup-summary').textContent = 'Buscá copias de icon.bin.';
  byId('smdh-backup').innerHTML = '<option value="">Cargá el workspace para buscar copias</option>';
  byId('smdh-backup').disabled = true;
  setSmdhRestoreResult('Elegí una copia para restaurarla.', 'neutral');
}

function loadSmdhForm(slot) {
  if (!smdhData) return;
  smdhSlot = Number(slot);
  const info = smdhData.appInfo?.[smdhSlot] || { shortDescription: '', longDescription: '', publisher: '' };
  byId('smdh-short').value = info.shortDescription || '';
  byId('smdh-long').value = info.longDescription || '';
  byId('smdh-publisher').value = info.publisher || '';
}

function captureSmdhForm() {
  if (!smdhData?.appInfo?.[smdhSlot]) return;
  smdhData.appInfo[smdhSlot].shortDescription = byId('smdh-short').value;
  smdhData.appInfo[smdhSlot].longDescription = byId('smdh-long').value;
  smdhData.appInfo[smdhSlot].publisher = byId('smdh-publisher').value;
}

function renderSmdh(data) {
  smdhData = data;
  const appInfo = (data.appInfo || []).filter((info) => info.shortDescription || info.longDescription || info.publisher);
  const gameVersion = data.gameVersion || 'juego detectado';
  byId('smdh-summary').textContent = `${gameVersion} · ${data.iconFile || 'icon.bin'} · ${appInfo.length} idioma(s) con datos`;
  const preview = byId('smdh-preview');
  preview.hidden = false;
  const small = byId('smdh-small-icon');
  const large = byId('smdh-large-icon');
  small.src = `data:image/png;base64,${data.smallIconPngBase64}`;
  large.src = `data:image/png;base64,${data.largeIconPngBase64}`;
  small.hidden = false;
  large.hidden = false;
  const list = byId('smdh-app-info');
  list.innerHTML = appInfo.length
    ? appInfo.map((info) => `<div class="title-screen-row is-valid"><div><b>Idioma ${Number(info.slot) + 1}</b><small>${escapeHtml(info.shortDescription || 'Sin nombre')} · ${escapeHtml(info.publisher || 'Sin editor')}</small><small>${escapeHtml(info.longDescription || 'Sin descripción')}</small></div><span>OK</span></div>`).join('')
    : '<div class="title-screen-row is-invalid"><div><b>Sin textos de aplicación</b><small>El icon.bin es válido, pero no contiene metadatos de idioma legibles.</small></div><span>Revisar</span></div>';
  list.hidden = false;
  const slotSelect = byId('smdh-slot');
  slotSelect.innerHTML = (data.appInfo || []).map((_, index) => `<option value="${index}">AppInfo ${index + 1}</option>`).join('');
  byId('smdh-editing').hidden = false;
  loadSmdhForm(0);
  loadSmdhSettings(data.settings);
  setSmdhUpdateResult('Podés editar los textos o indicar PNG nuevos.', 'neutral');
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

function resetTitleScreenBackups() {
  titleScreenBackups = null;
  byId('title-screen-backup-summary').textContent = 'Cargá el workspace para buscar copias.';
  byId('title-screen-backup').innerHTML = '<option value="">Cargá el workspace para buscar copias</option>';
  byId('title-screen-backup').disabled = true;
  setTitleScreenRestoreResult('Elegí una copia para restaurarla.', 'neutral');
}

function renderTitleScreenBackups(data) {
  titleScreenBackups = data;
  const backupSelect = byId('title-screen-backup');
  backupSelect.innerHTML = data.backups.length
    ? data.backups.map((backup) => {
      const created = new Date(backup.createdUtc).toLocaleString();
      return `<option value="${escapeHtml(backup.file)}">${created} · ${Number(backup.bytes).toLocaleString()} B</option>`;
    }).join('')
    : '<option value="">No hay copias disponibles</option>';
  backupSelect.disabled = data.backups.length === 0;
  byId('title-screen-backup-summary').textContent = data.backups.length
    ? `${data.backups.length} copia(s) encontrada(s) para ${data.gameVersion}.`
    : 'No hay copias de seguridad de pantalla de título.';
  updateBuildState();
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
      ? `${archive.assets.length} BCLIM · ${archive.darcBytes.toLocaleString()} bytes DARC${archive.compressed ? ' · LZSS' : ''}${archive.darcPrefixBytes || archive.darcSuffixBytes ? ` · envoltura ${archive.darcPrefixBytes || 0} + ${archive.darcSuffixBytes || 0} B` : ''}`
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
  byId('rebuild-crr-action').disabled = !inspected;
  byId('rebuild-cia-action').disabled = !inspected || !hasExeFs || !hasExheader;
  byId('create-patch').disabled = !inspected || !hasExeFs;
  byId('inspect-smdh').disabled = !inspected || !hasExeFs;
  byId('export-smdh').disabled = !smdhData;
  byId('update-smdh').disabled = !smdhData;
  byId('import-smdh').disabled = !inspected || !hasExeFs || !byId('smdh-import-input').value.trim();
  byId('load-smdh-backups').disabled = !inspected || !hasExeFs;
  byId('restore-smdh').disabled = !smdhBackups
    || byId('smdh-backup').disabled
    || !byId('smdh-backup').value;
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
  byId('load-title-screen-backups').disabled = !inspected;
  byId('restore-title-screen').disabled = !titleScreenBackups
    || byId('title-screen-backup').disabled
    || !byId('title-screen-backup').value;
  byId('convert-image').disabled = !byId('image-input').value.trim();
  const imageInput = byId('image-input').value.trim().toLowerCase();
  byId('image-format').disabled = Boolean(imageInput)
    && !imageInput.endsWith('.png')
    && !imageInput.endsWith('.bflim');
  byId('process-lz11').disabled = !byId('lz11-input').value.trim();
  byId('process-blz').disabled = !byId('blz-input').value.trim();
  byId('auto-unpack').disabled = !byId('auto-unpack-input').value.trim();
  byId('auto-pack').disabled = !byId('auto-pack-input').value.trim();
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
  resetSmdh();
  titleScreenCatalog = null;
  resetTitleScreenBackups();
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
    const codeIssue = (data.diagnostics || []).find((diagnostic) => diagnostic.code === 'code-bin' && ['warning', 'error'].includes(diagnostic.severity));
    const codeNote = codeIssue ? ` ${codeIssue.message}` : data.codeBinCompressed ? ' code.bin BLZ detectado; se descomprime en memoria.' : '';
    setStatus(`Listo: ${data.gameVersion}.${exefs}${codeNote}`, codeIssue?.severity === 'error' ? 'error' : 'success');
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

byId('browse-crr-output').addEventListener('click', async () => {
  const button = byId('browse-crr-output');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('crr-output').value = data.path;
  } catch (error) {
    setCrrResult(error.message, 'error');
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

byId('browse-auto-unpack-input').addEventListener('click', async () => {
  const button = byId('browse-auto-unpack-input');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-archive');
    byId('auto-unpack-input').value = data.path;
    updateBuildState();
  } catch (error) {
    setAutoUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-auto-unpack-output').addEventListener('click', async () => {
  const button = byId('browse-auto-unpack-output');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('auto-unpack-output').value = data.path;
  } catch (error) {
    setAutoUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-auto-pack-input').addEventListener('click', async () => {
  const button = byId('browse-auto-pack-input');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('auto-pack-input').value = data.path;
    updateBuildState();
  } catch (error) {
    setAutoPackResult(error.message, 'error');
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

byId('browse-garc-shuffle-input').addEventListener('click', async () => {
  const button = byId('browse-garc-shuffle-input');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-archive');
    byId('garc-shuffle-input').value = data.path;
  } catch (error) {
    setGarcShuffleResult(error.message, 'error');
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

byId('browse-darc-template').addEventListener('click', async () => {
  const button = byId('browse-darc-template');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-archive');
    byId('darc-template').value = data.path;
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

byId('browse-mini-input').addEventListener('click', async () => {
  const button = byId('browse-mini-input');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-archive');
    byId('mini-input').value = data.path;
  } catch (error) {
    setMiniUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-mini-unpack-output').addEventListener('click', async () => {
  const button = byId('browse-mini-unpack-output');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('mini-unpack-output').value = data.path;
  } catch (error) {
    setMiniUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-mini-folder').addEventListener('click', async () => {
  const button = byId('browse-mini-folder');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('mini-folder').value = data.path;
  } catch (error) {
    setMiniPackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-mini-template').addEventListener('click', async () => {
  const button = byId('browse-mini-template');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-archive');
    byId('mini-template').value = data.path;
  } catch (error) {
    setMiniPackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-alyt-input').addEventListener('click', async () => {
  const button = byId('browse-alyt-input');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-archive');
    byId('alyt-input').value = data.path;
  } catch (error) {
    setAlytResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-alyt-output').addEventListener('click', async () => {
  const button = byId('browse-alyt-output');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('alyt-output').value = data.path;
  } catch (error) {
    setAlytResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-alyt-folder').addEventListener('click', async () => {
  const button = byId('browse-alyt-folder');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('alyt-folder').value = data.path;
  } catch (error) {
    setAlytPackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-shuffle-arc-input').addEventListener('click', async () => {
  const button = byId('browse-shuffle-arc-input');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-archive');
    byId('shuffle-arc-input').value = data.path;
  } catch (error) {
    setShuffleArcResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-shuffle-arc-output').addEventListener('click', async () => {
  const button = byId('browse-shuffle-arc-output');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('shuffle-arc-output').value = data.path;
  } catch (error) {
    setShuffleArcResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-gar-input').addEventListener('click', async () => {
  const button = byId('browse-gar-input');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-archive');
    byId('gar-input').value = data.path;
  } catch (error) {
    setGarResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('browse-gar-output').addEventListener('click', async () => {
  const button = byId('browse-gar-output');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('gar-output').value = data.path;
  } catch (error) {
    setGarResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('inspect-action').addEventListener('click', inspectWorkspace);
workspace.addEventListener('input', () => {
  inspected = false;
  inspectedData = null;
  resetSmdh();
  titleScreenCatalog = null;
  resetTitleScreenBackups();
  byId('title-screen-catalog').hidden = true;
  byId('title-screen-summary').textContent = 'Cargá un workspace para analizarlo.';
  resetTitleScreenReplacementSelectors();
  setTitleScreenReplaceResult('Analizá el workspace para elegir una imagen.', 'neutral');
  setStatus('La ruta cambió. Cargala otra vez antes de construir.');
  updateBuildState();
});
document.querySelectorAll('#romfs, #exefs').forEach((control) => control.addEventListener('change', updateBuildState));
byId('auto-unpack-input').addEventListener('input', updateBuildState);
byId('auto-pack-input').addEventListener('input', updateBuildState);

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

byId('rebuild-crr-action').addEventListener('click', async () => {
  const button = byId('rebuild-crr-action');
  button.disabled = true;
  setCrrResult('Verificando CRO y reconstruyendo static.crr…');
  try {
    const data = await post('/api/workspace/rebuild-crr', {
      workspacePath: workspace.value,
      outputDirectory: byId('crr-output').value.trim() || null,
    });
    const files = data.changedFiles.length;
    setCrrResult(`Listo: ${data.croCount} CRO revisados, ${data.rehashedCros} rehash(es), ${data.crrChanged ? 'CRR actualizado' : 'CRR sin cambios'}. ${files ? `${files} archivo(s) en ${data.zipPath}.` : 'No había cambios que aplicar.'}`, 'success');
  } catch (error) {
    setCrrResult(error.message, 'error');
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

byId('auto-unpack').addEventListener('click', async () => {
  const button = byId('auto-unpack');
  button.disabled = true;
  setAutoUnpackResult('Detectando el formato y desempaquetando…');
  try {
    const data = await post('/api/workspace/unpack-auto', {
      inputFile: byId('auto-unpack-input').value,
      outputDirectory: byId('auto-unpack-output').value.trim() || null,
      skipDecompression: byId('auto-unpack-skip-decompression').checked,
      recursive: byId('auto-unpack-recursive').checked,
    });
    const identifier = data.identifier ? ` (${data.identifier})` : '';
    setAutoUnpackResult(`Listo: ${data.format}${identifier}, ${data.files} archivo(s), ${data.bytes.toLocaleString()} bytes en ${data.outputDirectory}.`, 'success');
  } catch (error) {
    setAutoUnpackResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('auto-pack').addEventListener('click', async () => {
  const button = byId('auto-pack');
  button.disabled = true;
  setAutoPackResult('Detectando el formato y empaquetando…');
  try {
    const data = await post('/api/workspace/pack-auto', {
      inputDirectory: byId('auto-pack-input').value,
      outputFile: byId('auto-pack-output').value.trim() || null,
      garcVersion: Number(byId('auto-pack-garc-version').value),
      garcBytesPadding: Number(byId('auto-pack-garc-padding').value),
    });
    const identifier = data.identifier ? ` (${data.identifier})` : '';
    setAutoPackResult(`Listo: ${data.format}${identifier}, ${data.files} archivo(s), ${data.bytes.toLocaleString()} bytes en ${data.outputFile}.`, 'success');
  } catch (error) {
    setAutoPackResult(error.message, 'error');
  } finally {
    updateBuildState();
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

byId('shuffle-garc').addEventListener('click', async () => {
  const button = byId('shuffle-garc');
  button.disabled = true;
  try {
    const rawSeed = byId('garc-shuffle-seed').value.trim();
    const data = await post('/api/workspace/shuffle-garc', {
      inputFile: byId('garc-shuffle-input').value,
      outputFile: byId('garc-shuffle-output').value.trim() || null,
      seed: rawSeed === '' ? null : Number(rawSeed),
    });
    setGarcShuffleResult(`Listo: ${data.changedEntries}/${data.shuffledEntries} referencias cambiadas. Salida: ${data.outputFile} (semilla ${data.seed}).`, 'success');
  } catch (error) {
    setGarcShuffleResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('unpack-mini').addEventListener('click', async () => {
  const button = byId('unpack-mini');
  button.disabled = true;
  setMiniUnpackResult('Desempaquetando el archivo Mini…');
  try {
    const data = await post('/api/workspace/unpack-mini', {
      inputFile: byId('mini-input').value,
      identifier: byId('mini-identifier').value,
      outputDirectory: byId('mini-unpack-output').value.trim() || null,
    });
    setMiniUnpackResult(`Listo: Mini ${data.identifier} con ${data.files} bloque(s) en ${data.outputDirectory}.`, 'success');
  } catch (error) {
    setMiniUnpackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('pack-mini').addEventListener('click', async () => {
  const button = byId('pack-mini');
  button.disabled = true;
  setMiniPackResult('Empaquetando el archivo Mini…');
  try {
    const data = await post('/api/workspace/pack-mini', {
      inputDirectory: byId('mini-folder').value,
      identifier: byId('mini-identifier').value,
      outputFile: byId('mini-pack-output').value.trim() || null,
      templateFile: byId('mini-template').value.trim() || null,
    });
    setMiniPackResult(`Listo: Mini ${data.identifier} con ${data.files} bloque(s), ${Number(data.bytes).toLocaleString()} bytes. ${data.note}`, 'success');
  } catch (error) {
    setMiniPackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('unpack-alyt').addEventListener('click', async () => {
  const button = byId('unpack-alyt');
  button.disabled = true;
  setAlytResult('Desempaquetando el ALYT y validando su SARC…');
  try {
    const data = await post('/api/workspace/unpack-alyt', {
      inputFile: byId('alyt-input').value,
      outputDirectory: byId('alyt-output').value.trim() || null,
    });
    setAlytResult(`Listo: ${data.files} archivo(s), ${data.labels} etiqueta(s) y ${data.symbols} símbolo(s) en ${data.outputDirectory}.`, 'success');
  } catch (error) {
    setAlytResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('pack-alyt').addEventListener('click', async () => {
  const button = byId('pack-alyt');
  button.disabled = true;
  setAlytPackResult('Empaquetando el SARC y envolviéndolo en ALYT…');
  try {
    const lines = (value) => value.split(/\r?\n/).map((line) => line.trim()).filter(Boolean);
    const labels = lines(byId('alyt-labels').value);
    const symbols = lines(byId('alyt-symbols').value);
    const data = await post('/api/workspace/pack-alyt', {
      inputDirectory: byId('alyt-folder').value,
      outputFile: byId('alyt-pack-output').value.trim() || null,
      labels: labels.length ? labels : null,
      symbols: symbols.length ? symbols : null,
    });
    setAlytPackResult(`Listo: ${data.files} archivo(s), ${data.labels} etiqueta(s) y ${data.symbols} símbolo(s), ${Number(data.bytes).toLocaleString()} bytes.`, 'success');
  } catch (error) {
    setAlytPackResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('unpack-shuffle-arc').addEventListener('click', async () => {
  const button = byId('unpack-shuffle-arc');
  button.disabled = true;
  setShuffleArcResult('Desempaquetando el Shuffle ARC…');
  try {
    const data = await post('/api/workspace/unpack-shuffle-arc', {
      inputFile: byId('shuffle-arc-input').value,
      outputDirectory: byId('shuffle-arc-output').value.trim() || null,
    });
    setShuffleArcResult(`Listo: ${data.files} fragmento(s), ${Number(data.bytes).toLocaleString()} bytes en ${data.outputDirectory}.`, 'success');
  } catch (error) {
    setShuffleArcResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('unpack-gar').addEventListener('click', async () => {
  const button = byId('unpack-gar');
  button.disabled = true;
  setGarResult('Desempaquetando el GAR…');
  try {
    const data = await post('/api/workspace/unpack-gar', {
      inputFile: byId('gar-input').value,
      outputDirectory: byId('gar-output').value.trim() || null,
    });
    setGarResult(`Listo: ${data.files} archivo(s), ${Number(data.bytes).toLocaleString()} bytes en ${data.outputDirectory}.`, 'success');
  } catch (error) {
    setGarResult(error.message, 'error');
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
      templateFile: byId('darc-template').value.trim() || null,
    });
    setDarcPackResult(`Listo: ${data.files} archivo(s), ${data.bytes.toLocaleString()} bytes. ${data.note}`, 'success');
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
      indexKind: Number(byId('farc-index-kind').value),
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

byId('browse-image-input').addEventListener('click', async () => {
  const button = byId('browse-image-input');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-image');
    byId('image-input').value = data.path;
    updateBuildState();
  } catch (error) {
    setImageResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('image-input').addEventListener('input', updateBuildState);

byId('browse-lz11-input').addEventListener('click', async () => {
  const button = byId('browse-lz11-input');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-any-file');
    byId('lz11-input').value = data.path;
    updateBuildState();
  } catch (error) {
    setLz11Result(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('lz11-input').addEventListener('input', updateBuildState);

byId('browse-blz-input').addEventListener('click', async () => {
  const button = byId('browse-blz-input');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-any-file');
    byId('blz-input').value = data.path;
    updateBuildState();
  } catch (error) {
    setBlzResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('blz-input').addEventListener('input', updateBuildState);

byId('title-screen-archive').addEventListener('change', updateTitleScreenAssets);
byId('title-screen-asset').addEventListener('change', loadTitleScreenPreview);
byId('title-screen-backup').addEventListener('change', updateBuildState);

byId('browse-smdh-output').addEventListener('click', async () => {
  const button = byId('browse-smdh-output');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-output');
    byId('smdh-output').value = data.path;
  } catch (error) {
    setSmdhResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

for (const [buttonId, inputId] of [['browse-smdh-small', 'smdh-small-input'], ['browse-smdh-large', 'smdh-large-input']]) {
  byId(buttonId).addEventListener('click', async () => {
    const button = byId(buttonId);
    button.disabled = true;
    try {
      const data = await post('/api/workspace/pick-image');
      byId(inputId).value = data.path;
    } catch (error) {
      setSmdhUpdateResult(error.message, 'error');
    } finally {
      button.disabled = false;
    }
  });
}

byId('browse-smdh-import').addEventListener('click', async () => {
  const button = byId('browse-smdh-import');
  button.disabled = true;
  try {
    const data = await post('/api/workspace/pick-smdh');
    byId('smdh-import-input').value = data.path;
    updateBuildState();
  } catch (error) {
    setSmdhImportResult(error.message, 'error');
  } finally {
    button.disabled = false;
  }
});

byId('smdh-import-input').addEventListener('input', updateBuildState);

byId('smdh-slot').addEventListener('change', () => {
  captureSmdhForm();
  loadSmdhForm(byId('smdh-slot').value);
});

byId('inspect-smdh').addEventListener('click', async () => {
  const button = byId('inspect-smdh');
  button.disabled = true;
  setSmdhResult('Leyendo ExeFS/icon.bin y generando las vistas PNG…');
  try {
    const data = await post('/api/workspace/smdh/inspect', { workspacePath: workspace.value });
    renderSmdh(data);
    setSmdhResult(`Listo: ${data.iconFile || 'icon.bin'} leído sin modificar el workspace.`, 'success');
  } catch (error) {
    resetSmdh();
    setSmdhResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('export-smdh').addEventListener('click', async () => {
  const button = byId('export-smdh');
  button.disabled = true;
  setSmdhResult('Exportando icon.bin y los iconos PNG…');
  try {
    const data = await post('/api/workspace/smdh/export', {
      workspacePath: workspace.value,
      outputDirectory: byId('smdh-output').value.trim() || null,
    });
    setSmdhResult(`Listo: icon.bin, small-icon.png y large-icon.png en ${data.outputDirectory}.`, 'success');
  } catch (error) {
    setSmdhResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('update-smdh').addEventListener('click', async () => {
  const button = byId('update-smdh');
  button.disabled = true;
  captureSmdhForm();
  setSmdhUpdateResult('Guardando los metadatos y los iconos…');
  try {
    const data = await post('/api/workspace/smdh/update', {
      workspacePath: workspace.value,
      appInfo: (smdhData.appInfo || []).map((info, slot) => ({
        slot,
        shortDescription: info.shortDescription || '',
        longDescription: info.longDescription || '',
        publisher: info.publisher || '',
      })),
      settings: captureSmdhSettings(),
      smallIconFile: byId('smdh-small-input').value.trim() || null,
      largeIconFile: byId('smdh-large-input').value.trim() || null,
    });
    setSmdhUpdateResult(`Listo: icon.bin actualizado. Backup: ${data.backupFile}.`, 'success');
    const inspectedAgain = await post('/api/workspace/smdh/inspect', { workspacePath: workspace.value });
    renderSmdh(inspectedAgain);
    setSmdhResult('Lectura actualizada después de aplicar los cambios.', 'success');
  } catch (error) {
    setSmdhUpdateResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('import-smdh').addEventListener('click', async () => {
  const button = byId('import-smdh');
  button.disabled = true;
  setSmdhImportResult('Validando e importando el SMDH completo…');
  try {
    const data = await post('/api/workspace/smdh/import', {
      workspacePath: workspace.value,
      sourceFile: byId('smdh-import-input').value.trim(),
    });
    setSmdhImportResult(`Listo: icon.bin importado. Backup: ${data.backupFile}.`, 'success');
    const inspectedAgain = await post('/api/workspace/smdh/inspect', { workspacePath: workspace.value });
    renderSmdh(inspectedAgain);
    setSmdhResult('Lectura actualizada después de importar el SMDH.', 'success');
  } catch (error) {
    setSmdhImportResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('load-smdh-backups').addEventListener('click', async () => {
  const button = byId('load-smdh-backups');
  button.disabled = true;
  setSmdhRestoreResult('Buscando copias de icon.bin…');
  try {
    const data = await post('/api/workspace/smdh/backups', { workspacePath: workspace.value });
    smdhBackups = data;
    const select = byId('smdh-backup');
    select.replaceChildren();
    if (data.backups.length === 0) {
      select.append(new Option('No hay copias disponibles', ''));
    } else {
      for (const backup of data.backups) {
        const created = new Date(backup.createdUtc).toLocaleString();
        select.append(new Option(`${created} · ${Number(backup.bytes).toLocaleString()} B`, backup.file));
      }
    }
    select.disabled = data.backups.length === 0;
    byId('smdh-backup-summary').textContent = data.note;
    setSmdhRestoreResult(data.note, data.backups.length ? 'success' : 'neutral');
  } catch (error) {
    smdhBackups = null;
    byId('smdh-backup').disabled = true;
    setSmdhRestoreResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('smdh-backup').addEventListener('change', updateBuildState);

byId('restore-smdh').addEventListener('click', async () => {
  const button = byId('restore-smdh');
  button.disabled = true;
  setSmdhRestoreResult('Validando y restaurando icon.bin…');
  try {
    const data = await post('/api/workspace/smdh/restore', {
      workspacePath: workspace.value,
      backupFile: byId('smdh-backup').value,
    });
    setSmdhRestoreResult(`Listo: icon.bin restaurado. Backup de seguridad: ${data.safetyBackupFile}.`, 'success');
    const inspectedAgain = await post('/api/workspace/smdh/inspect', { workspacePath: workspace.value });
    renderSmdh(inspectedAgain);
    setSmdhResult('Lectura actualizada después de restaurar el backup.', 'success');
  } catch (error) {
    setSmdhRestoreResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

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

byId('load-title-screen-backups').addEventListener('click', async () => {
  const button = byId('load-title-screen-backups');
  button.disabled = true;
  setTitleScreenRestoreResult('Buscando copias de seguridad…');
  try {
    const data = await post('/api/editors/titlescreen/backups', { workspacePath: workspace.value });
    renderTitleScreenBackups(data);
    setTitleScreenRestoreResult(data.note, data.backups.length ? 'success' : 'neutral');
  } catch (error) {
    resetTitleScreenBackups();
    setTitleScreenRestoreResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('restore-title-screen').addEventListener('click', async () => {
  const button = byId('restore-title-screen');
  button.disabled = true;
  setTitleScreenRestoreResult('Restaurando la copia…');
  try {
    const data = await post('/api/editors/titlescreen/restore', {
      workspacePath: workspace.value,
      backupFile: byId('title-screen-backup').value,
    });
    setTitleScreenRestoreResult(`Listo. Se restauró la copia y se guardó el estado anterior en ${data.safetyBackupFile}.`, 'success');
    titleScreenCatalog = null;
    byId('title-screen-catalog').hidden = true;
    resetTitleScreenReplacementSelectors();
    updateBuildState();
  } catch (error) {
    setTitleScreenRestoreResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('convert-image').addEventListener('click', async () => {
  const button = byId('convert-image');
  button.disabled = true;
  setImageResult('Convirtiendo la imagen…');
  try {
    const data = await post('/api/workspace/convert-image', {
      inputFile: byId('image-input').value.trim(),
      outputFile: byId('image-output').value.trim() || null,
      bclimFormat: byId('image-format').value,
    });
    setImageResult(`Listo: ${data.width}×${data.height}, ${data.inputFormat} → ${data.outputFormat}. Salida: ${data.outputFile}.`, 'success');
  } catch (error) {
    setImageResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('process-lz11').addEventListener('click', async () => {
  const button = byId('process-lz11');
  button.disabled = true;
  setLz11Result('Procesando el archivo LZ11…');
  try {
    const data = await post('/api/workspace/lz11', {
      inputFile: byId('lz11-input').value.trim(),
      operation: byId('lz11-operation').value,
      outputFile: byId('lz11-output').value.trim() || null,
    });
    const action = data.operation === 'compress' ? 'comprimido' : 'descomprimido';
    setLz11Result(`Listo: archivo ${action}. Salida: ${data.outputFile} (${Number(data.bytes).toLocaleString()} B).`, 'success');
  } catch (error) {
    setLz11Result(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

byId('process-blz').addEventListener('click', async () => {
  const button = byId('process-blz');
  button.disabled = true;
  setBlzResult('Procesando el archivo BLZ…');
  try {
    const data = await post('/api/workspace/blz', {
      inputFile: byId('blz-input').value.trim(),
      operation: byId('blz-operation').value,
      outputFile: byId('blz-output').value.trim() || null,
      bestCompression: byId('blz-best').checked,
      arm9: byId('blz-arm9').checked,
    });
    const action = data.operation === 'compress' ? 'comprimido' : 'descomprimido';
    setBlzResult(`Listo: archivo ${action}. Salida: ${data.outputFile} (${Number(data.bytes).toLocaleString()} B).`, 'success');
  } catch (error) {
    setBlzResult(error.message, 'error');
  } finally {
    updateBuildState();
  }
});

updateBuildState();
