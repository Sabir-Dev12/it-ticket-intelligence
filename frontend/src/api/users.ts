import { z } from 'zod';
import { request } from './client';
export const accountRoles = ['User','Agent','Support','Admin'] as const;
export const createAccountSchema=z.object({name:z.string().trim().min(1,'Enter a name.').max(100),email:z.string().trim().email('Enter a valid email address.').max(254),password:z.string().min(14,'Use at least 14 characters.').max(256).refine(v=>v.trim().length>0,'Enter a password.'),confirmation:z.string(),role:z.enum(accountRoles)}).refine(v=>v.password===v.confirmation,{path:['confirmation'],message:'Passwords must match.'});
export type AccountRole=typeof accountRoles[number];
export interface UserAccount {id:string;name:string;email:string;role:AccountRole;isActive:boolean}
export const usersApi={
  list:(signal?:AbortSignal)=>request<UserAccount[]>('/admin/users',signal),
  create:(input:z.infer<typeof createAccountSchema>,signal?:AbortSignal)=>{const {confirmation,...body}=input;void confirmation;return request<UserAccount>('/admin/users',signal,15000,{method:'POST',body});},
};
