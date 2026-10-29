import { describe,it,expect } from 'vitest';
import { createAccountSchema } from './users';
const valid={name:'Jane Agent',email:'jane@example.test',password:'sample-password-for-tests',confirmation:'sample-password-for-tests',role:'User'};
describe('account creation validation',()=>{
  it('trims identity fields and accepts all supported roles',()=>{for(const role of ['User','Agent','Support','Admin'])expect(createAccountSchema.parse({...valid,name:' Jane ',email:' jane@example.test ',role})).toMatchObject({name:'Jane',email:'jane@example.test',role});});
  it('rejects short or whitespace passwords',()=>{for(const password of ['short',' '.repeat(20)])expect(createAccountSchema.safeParse({...valid,password,confirmation:password}).success).toBe(false);});
  it('requires confirmation',()=>expect(createAccountSchema.safeParse({...valid,confirmation:'different'}).success).toBe(false));
  it('rejects unsupported roles and invalid identity fields',()=>{for(const input of [{role:'SuperAdmin'},{email:'invalid'},{name:'   '}])expect(createAccountSchema.safeParse({...valid,...input}).success).toBe(false);});
});
