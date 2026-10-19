import fs from 'node:fs';
import { inspectManagedMembersBytes } from '~/.local/lib/node_modules/rea-agents/dist/dotnet/ManagedMemberInspector.js';
import { decodeInstructions } from '~/.local/lib/node_modules/rea-agents/dist/dotnet/ManagedMemberInstructionDecoder.js';
const path = '~/Library/Application Support/Steam/steamapps/common/ROUNDS/ROUNDS.app/Contents/Resources/Data/Managed/Assembly-CSharp.dll';
const bytes = fs.readFileSync(path);
const want = process.argv.slice(2);
import crypto from 'node:crypto'; const sha256 = crypto.createHash('sha256').update(bytes).digest('hex'); const data = inspectManagedMembersBytes(bytes, { path, sha256, byte_length: bytes.length, format: 'pe' }, { typeNames: want });
const sym = new Map();
for (const key of ['methods','fields','member_refs']) for (const x of data[key] ?? []) sym.set(x.token, (x.declaring_type ?? x.parent_name ?? '?').split('/').pop() + '.' + x.name);
for (const x of data.types) sym.set(x.token, x.full_name);
for (const x of data.user_strings?.literals ?? []) sym.set(x.token, JSON.stringify(x.value));
for (const m of data.methods) {
  if (!/MoveNext|Update|Show|ChangeSelected/.test(m.name)) continue;
  const b = m.body; if (!b || b.status !== 'present') continue;
  const o = b.file_offset; const h = b.header_format === 'tiny' ? 1 : (bytes.readUInt16LE(o) >>> 12) * 4;
  const d = decodeInstructions(bytes.subarray(o + h, o + h + b.il_size));
  console.log('### ' + m.declaring_type + '.' + m.name);
  console.log(d.parsed.filter(i => i.opcode !== 'nop' && !/^(ldarg|ldloc|stloc|dup|pop|ldnull|conv)/.test(i.opcode)).map(i => i.offset + ' ' + i.opcode + ' ' + (sym.get(i.operand) ?? i.operand ?? '')).join(' | '));
}
