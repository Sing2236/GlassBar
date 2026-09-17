create extension if not exists citext;
create extension if not exists pgcrypto;

create or replace function public.request_user_id()
returns uuid
language sql
stable
as $$ select auth.uid() $$;

create or replace function public.request_has_mfa()
returns boolean
language sql
stable
as $$ select coalesce(auth.jwt() ->> 'aal', 'aal1') = 'aal2' $$;

create table if not exists public.profiles (
  user_id uuid primary key references auth.users(id) on delete cascade,
  username citext not null unique check (username ~ '^[a-zA-Z0-9_]{3,24}$'),
  avatar_url text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create or replace function public.handle_new_user()
returns trigger
language plpgsql
security definer
set search_path = ''
as $$
declare
  requested_username text;
begin
  requested_username := coalesce(new.raw_user_meta_data ->> 'username', '');
  if requested_username !~ '^[a-zA-Z0-9_]{3,24}$' then
    raise exception 'A valid username is required';
  end if;

  insert into public.profiles (user_id, username)
  values (new.id, requested_username);
  return new;
end;
$$;

drop trigger if exists on_auth_user_created on auth.users;
create trigger on_auth_user_created
after insert on auth.users
for each row execute procedure public.handle_new_user();

create table if not exists public.designs (
  id uuid primary key default gen_random_uuid(),
  author_id uuid not null references public.profiles(user_id) on delete cascade,
  kind text not null check (kind in ('glassbar', 'widget', 'animation')),
  name text not null check (char_length(name) between 1 and 60),
  slug text not null unique check (slug ~ '^[a-z0-9-]{3,80}$'),
  summary text not null default '' check (char_length(summary) <= 180),
  tags text[] not null default '{}',
  document jsonb not null check (
    jsonb_typeof(document) = 'object'
    and (document ->> 'schemaVersion')::integer = 1
    and document ->> 'kind' = kind
    and jsonb_typeof(document -> 'metadata') = 'object'
  ),
  preview_url text,
  is_published boolean not null default false,
  status text not null default 'pending' check (status in ('draft', 'pending', 'approved', 'rejected')),
  downloads integer not null default 0 check (downloads >= 0),
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  published_at timestamptz
);

create index if not exists designs_public_feed on public.designs(kind, published_at desc)
where is_published and status = 'approved';
create index if not exists designs_author on public.designs(author_id, updated_at desc);

alter table public.profiles enable row level security;
alter table public.designs enable row level security;

create policy "Public profiles are readable"
on public.profiles for select
using (true);

create policy "MFA users create their own profile"
on public.profiles for insert to authenticated
with check (user_id = public.request_user_id() and public.request_has_mfa());

create policy "MFA users update their own profile"
on public.profiles for update to authenticated
using (user_id = public.request_user_id() and public.request_has_mfa())
with check (user_id = public.request_user_id() and public.request_has_mfa());

create policy "Approved designs are public and authors see their own"
on public.designs for select
using ((is_published and status = 'approved') or author_id = public.request_user_id());

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
with check (author_id = public.request_user_id() and public.request_has_mfa() and status in ('draft', 'pending', 'rejected'));

create policy "MFA authors delete unapproved designs"
on public.designs for delete to authenticated
using (author_id = public.request_user_id() and public.request_has_mfa() and status <> 'approved');

insert into storage.buckets (id, name, public, file_size_limit, allowed_mime_types)
values (
  'design-assets',
  'design-assets',
  true,
  5000000,
  array['image/png', 'image/jpeg', 'image/webp', 'image/gif']
)
on conflict (id) do update set
  public = excluded.public,
  file_size_limit = excluded.file_size_limit,
  allowed_mime_types = excluded.allowed_mime_types;

create policy "MFA creators upload raster previews"
on storage.objects for insert to authenticated
with check (
  bucket_id = 'design-assets'
  and public.request_has_mfa()
  and (storage.foldername(name))[1] = auth.uid()::text
  and lower(storage.extension(name)) in ('png', 'jpg', 'jpeg', 'webp', 'gif')
);

create policy "Creators update their own previews"
on storage.objects for update to authenticated
using (bucket_id = 'design-assets' and (storage.foldername(name))[1] = auth.uid()::text and public.request_has_mfa())
with check (bucket_id = 'design-assets' and (storage.foldername(name))[1] = auth.uid()::text and public.request_has_mfa());

create policy "Creators delete their own previews"
on storage.objects for delete to authenticated
using (bucket_id = 'design-assets' and (storage.foldername(name))[1] = auth.uid()::text and public.request_has_mfa());

grant select on public.profiles, public.designs to anon, authenticated;
grant insert, update on public.profiles to authenticated;
grant insert, update, delete on public.designs to authenticated;
grant execute on function public.request_user_id(), public.request_has_mfa() to anon, authenticated;
revoke all on function public.handle_new_user() from public, anon, authenticated;
