alter table public.designs
  add column if not exists security_status text not null default 'not_required'
    check (security_status in ('not_required', 'passed', 'blocked')),
  add column if not exists security_report jsonb;

drop policy if exists "MFA authors create designs" on public.designs;
create policy "MFA authors create non-code designs"
on public.designs for insert to authenticated
with check (
  author_id = public.request_user_id()
  and public.request_has_mfa()
  and status in ('draft', 'pending')
  and not (
    kind = 'widget'
    and coalesce(document #>> '{widget,mode}', 'visual') = 'code'
  )
);

drop policy if exists "MFA authors edit unapproved designs" on public.designs;
create policy "MFA authors edit unapproved non-code designs"
on public.designs for update to authenticated
using (author_id = public.request_user_id() and public.request_has_mfa() and status <> 'approved')
with check (
  author_id = public.request_user_id()
  and public.request_has_mfa()
  and status in ('draft', 'pending', 'rejected')
  and not (
    kind = 'widget'
    and coalesce(document #>> '{widget,mode}', 'visual') = 'code'
  )
);
