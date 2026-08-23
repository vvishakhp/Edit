#!/usr/bin/env node
'use strict';

/**
 * Fingerprint / plan / index helpers for Tree-sitter native builds.
 *
 * Usage:
 *   node scripts/tree-sitter-natives.js plan --rid linux-x64 [--index native/index.json] [--force]
 *   node scripts/tree-sitter-natives.js write-index --rid linux-x64 --dir native/linux-x64 --index native/index.json
 *   node scripts/tree-sitter-natives.js manifest-hash
 */

const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const CLI_VERSION = process.env.TREE_SITTER_CLI_VERSION || '0.26.13';
const MANIFEST_PATH = process.env.TREE_SITTER_MANIFEST || path.join(ROOT, 'config', 'tree-sitter-grammars.json');

const BUILD_INPUTS = [
  'scripts/build-tree-sitter.sh',
  'scripts/build-tree-sitter-linux.sh',
  'scripts/tree-sitter-natives.js',
  'scripts/update-tree-sitter-natives.sh',
  'docker/tree-sitter-linux/Dockerfile',
  'docker/tree-sitter-linux/build.sh',
];

function sha256(text) {
  return crypto.createHash('sha256').update(text).digest('hex');
}

function fileHash(relPath) {
  const abs = path.join(ROOT, relPath);
  if (!fs.existsSync(abs)) return '';
  return sha256(fs.readFileSync(abs));
}

function buildInputsHash() {
  const parts = BUILD_INPUTS.map((rel) => `${rel}:${fileHash(rel)}`);
  return sha256(parts.join('\n'));
}

function libraryName(library, rid) {
  const base = library.replace(/\.(so|dll|dylib)$/i, '');
  const libBase = base.startsWith('lib') ? base : `lib${base}`;
  switch (rid) {
    case 'win-x64':
    case 'win-x86':
      return `${libBase.replace(/^lib/, '')}.dll`;
    case 'osx-arm64':
    case 'osx-x64':
      return `${libBase}.dylib`;
    default:
      return `${libBase}.so`;
  }
}

function loadManifest() {
  return JSON.parse(fs.readFileSync(MANIFEST_PATH, 'utf8'));
}

function grammarFingerprint(grammar, rid, inputsHash) {
  const library = libraryName(grammar.library, rid);
  const payload = [
    grammar.id,
    grammar.repository,
    grammar.ref,
    library,
    grammar.subpath || '',
    grammar.function || '',
    CLI_VERSION,
    rid,
    inputsHash,
  ].join('\n');
  return sha256(payload);
}

function desiredIndex(rid) {
  const source = loadManifest();
  const inputsHash = buildInputsHash();
  const grammars = {};
  for (const grammar of source.grammars) {
    const library = libraryName(grammar.library, rid);
    grammars[grammar.id] = {
      fingerprint: grammarFingerprint(grammar, rid, inputsHash),
      library,
      repository: grammar.repository,
      ref: grammar.ref,
      subpath: grammar.subpath || null,
    };
  }
  return {
    cliVersion: CLI_VERSION,
    buildInputsHash: inputsHash,
    manifestHash: sha256(fs.readFileSync(MANIFEST_PATH)),
    rids: {
      [rid]: { grammars },
    },
  };
}

function loadIndex(indexPath) {
  if (!indexPath || !fs.existsSync(indexPath)) return null;
  return JSON.parse(fs.readFileSync(indexPath, 'utf8'));
}

function libraryExists(nativeDir, grammarId, library) {
  return fs.existsSync(path.join(nativeDir, grammarId, library));
}

function plan(rid, indexPath, nativeDir, force) {
  const desired = desiredIndex(rid);
  const existing = loadIndex(indexPath);
  const existingGrammars = existing?.rids?.[rid]?.grammars || {};
  const rebuild = [];
  const keep = [];

  const inputsChanged =
    force ||
    !existing ||
    existing.cliVersion !== CLI_VERSION ||
    existing.buildInputsHash !== desired.buildInputsHash;

  for (const [id, meta] of Object.entries(desired.rids[rid].grammars)) {
    const prev = existingGrammars[id];
    const hasBinary = libraryExists(nativeDir, id, meta.library);
    if (inputsChanged || !prev || prev.fingerprint !== meta.fingerprint || !hasBinary) {
      rebuild.push(id);
    } else {
      keep.push(id);
    }
  }

  const removed = Object.keys(existingGrammars).filter((id) => !(id in desired.rids[rid].grammars));

  return {
    rid,
    force: Boolean(force),
    inputsChanged: Boolean(inputsChanged && !force ? existing && existing.buildInputsHash !== desired.buildInputsHash : inputsChanged),
    rebuild,
    keep,
    removed,
    desired,
  };
}

function writeIndex(rid, nativeDir, indexPath) {
  const desired = desiredIndex(rid);
  const existing = loadIndex(indexPath) || {
    cliVersion: CLI_VERSION,
    buildInputsHash: desired.buildInputsHash,
    manifestHash: desired.manifestHash,
    rids: {},
  };

  // Drop grammars whose binaries are missing from disk.
  const grammars = {};
  for (const [id, meta] of Object.entries(desired.rids[rid].grammars)) {
    if (libraryExists(nativeDir, id, meta.library)) {
      grammars[id] = meta;
    }
  }

  existing.cliVersion = CLI_VERSION;
  existing.buildInputsHash = desired.buildInputsHash;
  existing.manifestHash = desired.manifestHash;
  existing.updatedAt = new Date().toISOString();
  existing.rids = existing.rids || {};
  existing.rids[rid] = { grammars };

  fs.mkdirSync(path.dirname(indexPath), { recursive: true });
  fs.writeFileSync(indexPath, `${JSON.stringify(existing, null, 2)}\n`);
  return existing;
}

function mergeIndexes(paths, outPath) {
  const merged = {
    cliVersion: CLI_VERSION,
    buildInputsHash: buildInputsHash(),
    manifestHash: sha256(fs.readFileSync(MANIFEST_PATH)),
    updatedAt: new Date().toISOString(),
    rids: {},
  };

  for (const p of paths) {
    if (!fs.existsSync(p)) continue;
    const idx = JSON.parse(fs.readFileSync(p, 'utf8'));
    for (const [rid, data] of Object.entries(idx.rids || {})) {
      merged.rids[rid] = data;
    }
  }

  fs.writeFileSync(outPath, `${JSON.stringify(merged, null, 2)}\n`);
  return merged;
}

function parseArgs(argv) {
  const args = { _: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a.startsWith('--')) {
      const key = a.slice(2);
      const next = argv[i + 1];
      if (!next || next.startsWith('--')) {
        args[key] = true;
      } else {
        args[key] = next;
        i++;
      }
    } else {
      args._.push(a);
    }
  }
  return args;
}

function main() {
  const args = parseArgs(process.argv.slice(2));
  const cmd = args._[0];

  if (cmd === 'manifest-hash') {
    process.stdout.write(`${sha256(fs.readFileSync(MANIFEST_PATH))}\n`);
    return;
  }

  if (cmd === 'plan') {
    const rid = args.rid;
    if (!rid) throw new Error('--rid is required');
    const nativeDir = args.dir || path.join(ROOT, 'native', rid);
    const indexPath = args.index || path.join(ROOT, 'native', 'index.json');
    const result = plan(rid, indexPath, nativeDir, Boolean(args.force));
    if (args['changed-file']) {
      fs.writeFileSync(args['changed-file'], `${result.rebuild.join('\n')}${result.rebuild.length ? '\n' : ''}`);
    }
    process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
    return;
  }

  if (cmd === 'write-index') {
    const rid = args.rid;
    if (!rid) throw new Error('--rid is required');
    const nativeDir = args.dir || path.join(ROOT, 'native', rid);
    const indexPath = args.index || path.join(ROOT, 'native', 'index.json');
    const result = writeIndex(rid, nativeDir, indexPath);
    process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
    return;
  }

  if (cmd === 'merge-index') {
    const files = String(args.files || '')
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);
    const out = args.out || path.join(ROOT, 'native', 'index.json');
    const result = mergeIndexes(files, out);
    process.stdout.write(`${JSON.stringify(result, null, 2)}\n`);
    return;
  }

  console.error(`Unknown or missing command. Use: plan | write-index | merge-index | manifest-hash`);
  process.exit(1);
}

main();
