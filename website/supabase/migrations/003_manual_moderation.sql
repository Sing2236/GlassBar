drop policy if exists "MFA authors create non-code designs" on public.designs;
drop policy if exists "MFA authors edit unapproved non-code designs" on public.designs;

create policy "MFA authors create designs"
on public.designs for insert to authenticated
with check (
  author_id = public.request_user_id()
  and public.request_has_mfa()
  and status in ('draft', 'pending')
);

create policy "MFA authors edit unapproved designs"
on public.designs for update to authenticated
using (author_id = public.request_user_id() and public.request_has_mfa() and status <> 'approved')
with check (
  author_id = public.request_user_id()
  and public.request_has_mfa()
  and status in ('draft', 'pending', 'rejected')
);

alter table public.designs alter column security_status set default 'not_required';
