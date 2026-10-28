import { homedir } from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
const reaRoot = process.env.REA_MODULE_ROOT ?? join(homedir(), '.local/lib/node_modules/rea-agents');
// Supplementary read-only decoder (reuses REA's installed CIL decoder over evidence E02).
// Usage: node ReverseAnalysis/tools/decode-extra-il.mjs <TypeName> [...]
import fs from 'node:fs';
import crypto from 'node:crypto';
const { decodeInstructions } = await import(pathToFileURL(join(reaRoot, 'dist/dotnet/ManagedMemberInstructionDecoder.js')).href);

const bundlePath = process.env.REA_EVIDENCE_BUNDLE;
if (!bundlePath) throw new Error('Set REA_EVIDENCE_BUNDLE to your local evidence JSON');
const bundle = JSON.parse(fs.readFileSync(bundlePath));
const parent = bundle.records.find(r => r.operation === 'inspect_managed_members');
const data = parent.normalized_result;
const bytes = fs.readFileSync(data.artifact.path);
const sha = b => crypto.createHash('sha256').update(b).digest('hex');
if (sha(bytes) !== data.artifact.sha256) throw Error('Artifact identity changed');
const symbols = new Map();
for (const key of ['methods', 'fields', 'member_refs']) for (const x of data[key]) symbols.set(x.token, (x.declaring_type ?? x.parent_name ?? '?') + '.' + x.name);
for (const x of data.types) symbols.set(x.token, x.full_name);
for (const x of data.user_strings.literals) symbols.set(x.token, JSON.stringify(x.value));
const selected = new Set(process.argv.slice(2));
let out = '';
for (const m of data.methods.filter(m => selected.has(m.declaring_type))) {
  const b = m.body; if (b.status !== 'present') continue;
  const o = b.file_offset;
  const h = b.header_format === 'tiny' ? 1 : (bytes.readUInt16LE(o) >>> 12) * 4;
  const il = bytes.subarray(o + h, o + h + b.il_size);
  if (sha(il) !== b.il_sha256) throw Error('IL identity changed: ' + m.token);
  const d = decodeInstructions(il);
  if (d.issue || d.truncated || d.parsed.length !== b.instruction_count) throw Error('Decode incomplete: ' + m.token);
  out += '\n### ' + m.declaring_type + '.' + m.name + ' ' + m.token + ' IL_SHA256=' + b.il_sha256 + '\n' +
    d.parsed.filter(i => !['nop'].includes(i.opcode)).map(i => String(i.offset).padStart(5) + ' ' + i.opcode.padEnd(14) + ' ' + (symbols.get(i.operand) ?? i.operand ?? '')).join('\n') + '\n';
}
for (const t of selected) {
  const fields = data.fields.filter(f => f.declaring_type === t).map(f => f.name);
  out = '## fields ' + t + ': ' + fields.join(', ') + '\n' + out;
}
process.stdout.write('source_evidence_id=' + parent.evidence_id + '\n' + out);

