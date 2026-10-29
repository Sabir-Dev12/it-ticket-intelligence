import { expect, it } from 'vitest';
import { knowledgeSchema, importKnowledgeText } from './knowledge';
const valid={title:'VPN guide',content:'Check the VPN client.',source:'vpn.md',category:'Network',kind:'Procedure',format:'Markdown'};
it('validates required document fields, enums and boundaries',()=>{
  expect(knowledgeSchema.safeParse(valid).success).toBe(true);
  for(const patch of [{title:' '},{content:''},{source:' '},{content:'a'.repeat(100001)},{format:'PDF'},{kind:'Unknown'},{content:'bad\0text'}])expect(knowledgeSchema.safeParse({...valid,...patch}).success).toBe(false);
});
it('imports bounded UTF-8 text without interpreting markup',async()=>{
  const result=await importKnowledgeText(new File(['# Guide\n<script>literal</script>'],'guide.md'));
  expect(result.format).toBe('Markdown');expect(result.content).toContain('<script>literal</script>');
  await expect(importKnowledgeText(new File(['pdf'],'guide.pdf'))).rejects.toThrow();
  await expect(importKnowledgeText(new File([new Uint8Array([255,254])],'guide.txt'))).rejects.toThrow();
  await expect(importKnowledgeText(new File([''],'empty.txt'))).rejects.toThrow();
});
