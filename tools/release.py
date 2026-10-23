#!/usr/bin/env python3
"""Script d'automatisation des releases Soulfract.

Permet de créer/publier une release GitHub pour le jeu Soulfract, en :
1. Committant automatiquement les modifications en cours
2. Créant un tag Git v*
3. Poussant le tag sur origin (déclenche le workflow GitHub Actions release.yml)
4. Attendant que le workflow termine le build et l'upload des assets
5. Créant/mettant à jour la release GitHub avec les notes de version

Nécessite :
- Python 3.10+
- Git Credential Manager authentifié sur le compte Mateo-Llr, ou GitHub CLI authentifié
- Ou un token GitHub dans la variable d'environnement GITHUB_TOKEN
- git configuré sur le remote origin
"""

import json
import os
import re
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.request
import zipfile
from datetime import datetime, timezone
from pathlib import Path

REPO = "Mateo-Llr/Soulfract"
API_BASE = f"https://api.github.com/repos/{REPO}"
GITHUB_ACCOUNT = "Mateo-Llr"
VERSION_RE = re.compile(r"^v?\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$")


def log(msg, level="INFO"):
    timestamp = datetime.now().strftime("%H:%M:%S")
    prefix = {"INFO": "[INFO]", "WARN": "[WARN]", "ERR": "[ERREUR]", "OK": "[OK]"}.get(level, "[INFO]")
    print(f"{timestamp} {prefix} {msg}")


def run(cmd, check=True):
    log(f"Exécution : {cmd}")
    result = subprocess.run(cmd, shell=True, capture_output=True, text=True)
    if result.stdout:
        print(result.stdout, end="")
    if result.stderr:
        print(result.stderr, end="")
    if check and result.returncode != 0:
        log(f"Échec de la commande (code {result.returncode})", "ERR")
        sys.exit(1)
    return result


def prompt(prompt_text, default=None):
    if default:
        prompt_text += f" [{default}] "
    else:
        prompt_text += " "
    val = input(prompt_text).strip()
    return val if val else default


def confirm(question):
    return (prompt(f"{question} (o/N) :") or "").lower() == "o"


def has_changes():
    """True s'il y a des modifications (suivi ou untracked non ignoré)."""
    result = subprocess.run(["git", "status", "--porcelain"], capture_output=True, text=True)
    return result.stdout.strip() != ""


def git_status_clean():
    return not has_changes()


def git_current_commit():
    result = subprocess.run(["git", "rev-parse", "HEAD"], capture_output=True, text=True)
    return result.stdout.strip()


def show_changes():
    result = subprocess.run(["git", "status", "--short"], capture_output=True, text=True)
    if result.stdout:
        print(result.stdout, end="")


def auto_commit(default_message):
    """Committe toutes les modifications avec un message demandé à l'utilisateur."""
    if not has_changes():
        log("Aucune modification à committer.", "INFO")
        return False

    log("Modifications détectées :")
    show_changes()

    msg = prompt("Message de commit", default=default_message)
    if not msg:
        log("Message de commit requis.", "ERR")
        sys.exit(1)

    run("git add -A")
    safe_msg = msg.replace('"', '\\"')
    run(f'git commit -m "{safe_msg}"')
    log(f"Commit créé : {msg}", "OK")
    return True


def tag_exists_locally(tag):
    result = subprocess.run(["git", "tag", "-l", tag], capture_output=True, text=True)
    return tag in result.stdout.split()


def _auth_headers(token):
    headers = {"Accept": "application/vnd.github+json", "User-Agent": "Soulfract-Release-Script"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    return headers


def _token_from_git_credential():
    env = os.environ.copy()
    env["GCM_INTERACTIVE"] = "never"
    env["GIT_TERMINAL_PROMPT"] = "0"
    result = subprocess.run(
        ["git", "credential", "fill"],
        input="protocol=https\nhost=github.com\n\n",
        capture_output=True,
        text=True,
        env=env,
    )
    if result.returncode != 0:
        return None

    credentials = {}
    for line in result.stdout.splitlines():
        key, separator, value = line.partition("=")
        if separator:
            credentials[key] = value
    if credentials.get("username", "").casefold() != GITHUB_ACCOUNT.casefold():
        return None
    return credentials.get("password")


def get_github_token():
    """Récupère un token sans l'afficher et vérifie qu'il appartient au compte attendu."""
    token = os.environ.get("GITHUB_TOKEN", "").strip()
    if not token:
        token = _token_from_git_credential() or ""

    if not token:
        gh_path = shutil.which("gh")
        if not gh_path:
            log("Aucun identifiant GitHub disponible. Connectez Git Credential Manager ou lancez : gh auth login --hostname github.com --user Mateo-Llr", "ERR")
            sys.exit(1)

        result = subprocess.run(
            [gh_path, "auth", "token", "--hostname", "github.com"],
            capture_output=True,
            text=True,
        )
        token = result.stdout.strip()
        if result.returncode != 0 or not token:
            log("GitHub CLI n'est pas authentifié. Lancez : gh auth login --hostname github.com --user Mateo-Llr", "ERR")
            sys.exit(1)

    request = urllib.request.Request(
        "https://api.github.com/user",
        headers=_auth_headers(token),
    )
    try:
        with urllib.request.urlopen(request, timeout=15) as response:
            account = json.loads(response.read().decode()).get("login")
    except (urllib.error.URLError, json.JSONDecodeError) as error:
        log(f"Impossible de vérifier l'authentification GitHub : {error}", "ERR")
        sys.exit(1)

    if account != GITHUB_ACCOUNT:
        log(f"Le token actif appartient à '{account}', pas à '{GITHUB_ACCOUNT}'. Basculez le compte avec gh auth switch --hostname github.com --user {GITHUB_ACCOUNT}.", "ERR")
        sys.exit(1)

    log(f"Authentifié sur GitHub en tant que {account}.", "OK")
    return token


def tag_exists_remote(token, tag):
    url = f"{API_BASE}/git/ref/tags/{tag}"
    req = urllib.request.Request(url, headers=_auth_headers(token))
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status == 200
    except urllib.error.HTTPError as e:
        if e.code == 404:
            return False
        raise


def create_local_tag(tag, commit):
    run(f'git tag -a {tag} {commit} -m "Release {tag}"')


def push_tag(tag):
    run(f"git push origin {tag}")


def get_release_by_tag(token, tag):
    url = f"{API_BASE}/releases/tags/{tag}"
    req = urllib.request.Request(url, headers=_auth_headers(token))
    try:
        with urllib.request.urlopen(req) as resp:
            return json.loads(resp.read().decode())
    except urllib.error.HTTPError as e:
        if e.code == 404:
            return None
        raise


def create_or_update_release(token, tag, name, body, commit_sha):
    """Crée ou met à jour une release GitHub pour le tag donné.

    Si aucune note n'est fournie et que la release existe déjà (créée par le workflow),
    on conserve ses notes générées automatiquement au lieu de les écraser par du vide.
    """
    existing = get_release_by_tag(token, tag)
    payload = {
        "tag_name": tag,
        "name": name,
        "draft": False,
        "prerelease": "-" in tag,
    }

    headers = _auth_headers(token)
    headers["Content-Type"] = "application/json"

    if existing:
        if body:
            payload["body"] = body
        url = f"{API_BASE}/releases/{existing['id']}"
        req = urllib.request.Request(url, data=json.dumps(payload).encode(), headers=headers, method="PATCH")
        log(f"Mise à jour de la release existante {tag} (id={existing['id']})")
    else:
        payload["target_commitish"] = commit_sha
        payload["body"] = body or ""
        payload["generate_release_notes"] = not body
        url = f"{API_BASE}/releases"
        req = urllib.request.Request(url, data=json.dumps(payload).encode(), headers=headers, method="POST")
        log(f"Création de la release {tag}")

    with urllib.request.urlopen(req) as resp:
        return json.loads(resp.read().decode())


def wait_for_workflow(token, tag, started_at, timeout=900):
    """Attend que le run du workflow déclenché par CE tag se termine.

    Retourne True (succès), False (échec) ou None (run introuvable / délai dépassé).
    """
    url = f"{API_BASE}/actions/workflows/release.yml/runs?event=push&per_page=10"
    deadline = time.time() + timeout
    while time.time() < deadline:
        req = urllib.request.Request(url, headers=_auth_headers(token))
        try:
            with urllib.request.urlopen(req) as resp:
                data = json.loads(resp.read().decode())
        except Exception as e:
            log(f"Impossible d'interroger les workflows : {e}", "WARN")
            time.sleep(10)
            continue

        for wf_run in data.get("workflow_runs", []):
            # Pour un push de tag, head_branch contient le nom du tag.
            if wf_run.get("head_branch") != tag:
                continue
            created = datetime.fromisoformat(wf_run["created_at"].replace("Z", "+00:00"))
            if created < started_at:
                continue  # run d'un ancien push du même tag
            status = wf_run.get("status")
            conclusion = wf_run.get("conclusion")
            log(f"Workflow {wf_run['id']} : status={status}, conclusion={conclusion}")
            if status == "completed":
                return conclusion == "success"
            break
        time.sleep(15)

    log("Délai d'attente dépassé pour le workflow", "WARN")
    return None


def clean_publish_dir(publish_dir):
    """Supprime les fichiers inutiles À LA RACINE du publish uniquement.

    Ne jamais utiliser rglob ici : Data/, assets/ et GuildEmblems/ contiennent des
    fichiers (.xml, ...) nécessaires au jeu.
    """
    for pattern in ("*.dylib", "*.so", "*.pdb"):
        for f in publish_dir.glob(pattern):
            f.unlink(missing_ok=True)


def validate_binary_version(binary_path, version):
    """Vérifie que les métadonnées Windows du binaire correspondent à la release."""
    expected_file_version = version.split("-", 1)[0] + ".0"
    script = r"""
$versionInfo = (Get-Item -LiteralPath $env:SOULFRACT_BINARY_PATH).VersionInfo
$productVersion = ($versionInfo.ProductVersion -split '\+')[0]
if ($productVersion -ne $env:SOULFRACT_VERSION -or $versionInfo.FileVersion -ne $env:SOULFRACT_FILE_VERSION) {
    Write-Error "$env:SOULFRACT_BINARY_PATH porte ProductVersion=$productVersion et FileVersion=$($versionInfo.FileVersion), attendu $env:SOULFRACT_VERSION / $env:SOULFRACT_FILE_VERSION"
    exit 1
}
"$env:SOULFRACT_BINARY_PATH OK: ProductVersion=$productVersion FileVersion=$($versionInfo.FileVersion)"
"""
    env = os.environ.copy()
    env["SOULFRACT_BINARY_PATH"] = str(binary_path)
    env["SOULFRACT_VERSION"] = version
    env["SOULFRACT_FILE_VERSION"] = expected_file_version
    result = subprocess.run(
        ["powershell", "-NoProfile", "-Command", script],
        capture_output=True,
        text=True,
        env=env,
    )
    if result.stdout:
        print(result.stdout, end="")
    if result.stderr:
        print(result.stderr, end="")
    if result.returncode != 0:
        log(f"Les métadonnées de version de {binary_path} ne correspondent pas à {version}.", "ERR")
        sys.exit(1)


def build_game_zip(version):
    """Construit le jeu et crée publish/Soulfract-win-x64.zip localement."""
    publish_dir = Path("publish") / "game"
    if publish_dir.exists():
        shutil.rmtree(publish_dir)
    run(
        "dotnet publish Heroes.csproj -c Release -r win-x64 --self-contained true "
        f"-p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -p:Version={version} "
        "-o publish/game"
    )
    validate_binary_version(publish_dir / "Soulfract.exe", version)
    clean_publish_dir(publish_dir)

    zip_path = Path("publish") / "Soulfract-win-x64.zip"
    if zip_path.exists():
        zip_path.unlink()
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for f in publish_dir.rglob("*"):
            if f.is_file():
                zf.write(f, f.relative_to(publish_dir))
    log(f"Archive créée : {zip_path}", "OK")
    return zip_path


def upload_release_asset(token, release_id, file_path):
    """Upload un fichier comme asset de la release (remplace s'il existe déjà)."""
    file_path = Path(file_path)
    name = file_path.name
    url = f"{API_BASE}/releases/{release_id}"
    req = urllib.request.Request(url, headers=_auth_headers(token))
    with urllib.request.urlopen(req) as resp:
        rel = json.loads(resp.read().decode())
    for a in rel.get("assets", []):
        if a["name"] == name:
            del_url = f"{API_BASE}/releases/assets/{a['id']}"
            dreq = urllib.request.Request(del_url, headers=_auth_headers(token), method="DELETE")
            try:
                urllib.request.urlopen(dreq)
            except Exception:
                pass
    upload_url = f"https://uploads.github.com/repos/{REPO}/releases/{release_id}/assets?name={name}"
    headers = _auth_headers(token)
    headers["Content-Type"] = "application/zip" if name.endswith(".zip") else "application/octet-stream"
    data = file_path.read_bytes()
    req = urllib.request.Request(upload_url, data=data, headers=headers, method="POST")
    with urllib.request.urlopen(req) as resp:
        log(f"Asset uploadé : {name}", "OK")
        return json.loads(resp.read().decode())


def build_launcher(version):
    publish_dir = Path("publish/launcher")
    if publish_dir.exists():
        shutil.rmtree(publish_dir)
    run(
        "dotnet publish Launcher/SoulfractLauncher.csproj -c Release -r win-x64 --self-contained true "
        f"-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -p:Version={version} "
        "-o publish/launcher"
    )
    launcher_path = publish_dir / "SoulfractLauncher.exe"
    validate_binary_version(launcher_path, version)
    return launcher_path


def build_bootstrapper(version):
    publish_dir = Path("publish/bootstrapper")
    if publish_dir.exists():
        shutil.rmtree(publish_dir)
    run(
        "dotnet publish Bootstrapper/SoulfractBootstrapper.csproj -c Release -r win-x64 "
        f"-p:PublishAot=true -p:StripSymbols=true -p:Version={version} -o publish/bootstrapper"
    )
    bootstrapper_path = publish_dir / "SoulfractBootstrapper.exe"
    validate_binary_version(bootstrapper_path, version)
    return bootstrapper_path


def release_assets(token, tag):
    """Affiche les assets de la release."""
    release = get_release_by_tag(token, tag)
    if not release:
        log("Release introuvable sur GitHub", "ERR")
        return False
    assets = release.get("assets", [])
    log(f"Assets de la release {tag} : {len(assets)}")
    for a in assets:
        log(f"  - {a['name']} ({a['size']} octets, {a.get('download_count', 0)} téléchargements)")
    return len(assets) > 0


def main():
    print("=" * 60)
    print("  SOULFRACT - Automatisation des releases GitHub")
    print("=" * 60)

    # --- 1. Version (format strict : 1.0.26 ou 1.0.26-beta)
    version = prompt("Numéro de version (ex: 1.0.26) :")
    if not version or not VERSION_RE.match(version):
        log("Version invalide. Format attendu : 1.0.26 (ou 1.0.26-beta). Pas de lettres collées au numéro.", "ERR")
        sys.exit(1)
    tag = version if version.startswith("v") else f"v{version}"
    clean_version = tag[1:]

    # Un tag/release ne doit jamais être réutilisé : GitHub peut conserver ses anciens assets.
    if tag_exists_locally(tag):
        log(f"Le tag {tag} existe déjà localement. Choisissez une nouvelle version.", "ERR")
        sys.exit(1)

    # --- 2. Authentification GitHub et vérification d'unicité distante
    token = get_github_token()
    if tag_exists_remote(token, tag):
        log(f"Le tag {tag} existe déjà sur GitHub. Choisissez une nouvelle version.", "ERR")
        sys.exit(1)
    if get_release_by_tag(token, tag):
        log(f"Une release {tag} existe déjà sur GitHub. Choisissez une nouvelle version.", "ERR")
        sys.exit(1)

    # --- 3. Commit automatique
    auto_commit(default_message=f"chore: préparatif release {tag}")

    # --- 4. L'arbre doit être propre avant de taguer
    if not git_status_clean():
        log("L'arbre de travail n'est toujours pas propre après commit.", "ERR")
        sys.exit(1)

    # --- 5. Nom / notes de la release
    release_name = prompt(f"Nom de la release (ex: Release {tag})", default=f"Release {tag}")
    body_lines = []
    print("Entrez les notes de version (ligne vide pour terminer ; vide = notes auto) :")
    while True:
        line = input("  > ")
        if line == "":
            break
        body_lines.append(line)
    body = "\n".join(body_lines)

    commit = git_current_commit()
    log(f"Commit cible : {commit}")

    # --- 6. Créer et pousser le tag (déclenche le workflow Actions)
    started_at = datetime.now(timezone.utc).replace(microsecond=0)
    create_local_tag(tag, commit)
    push_tag(tag)
    log(f"Tag {tag} poussé sur origin. Le workflow de build devrait se déclencher.")

    # --- 7. Attendre le workflow
    workflow_ok = None
    log("Attente de la fin du workflow GitHub Actions...")
    workflow_ok = wait_for_workflow(token, tag, started_at)
    if workflow_ok:
        log("Workflow terminé avec succès.", "OK")
    else:
        state = "a échoué" if workflow_ok is False else "n'a pas été détecté / a expiré"
        log(f"Le workflow {state}. Consultez l'onglet Actions sur GitHub.", "ERR")
        log("Le build local de secours ne génère PAS l'archive différentielle (delta).", "WARN")
        if not confirm("Publier quand même avec un build local de secours ?"):
            log("Arrêt. Corrigez le workflow, supprimez le tag et relancez.", "WARN")
            sys.exit(1)

    # --- 8. Créer/mettre à jour la release
    rel = create_or_update_release(token, tag, release_name, body, commit)
    log(f"Release {tag} publiée sur GitHub (id={rel['id']})", "OK")
    log(f"URL : {rel.get('html_url')}")

    # Vérifier les assets et compléter localement si absents
    time.sleep(3)
    rel2 = get_release_by_tag(token, tag)
    asset_names = [a["name"] for a in rel2.get("assets", [])]
    if "Soulfract-win-x64.zip" not in asset_names:
        log("Soulfract-win-x64.zip absent de la release, création locale...", "WARN")
        upload_release_asset(token, rel2["id"], build_game_zip(clean_version))
    if "SoulfractLauncher.exe" not in asset_names:
        log("SoulfractLauncher.exe absent de la release, création locale...", "WARN")
        upload_release_asset(token, rel2["id"], build_launcher(clean_version))
    if "SoulfractBootstrapper.exe" not in asset_names:
        log("SoulfractBootstrapper.exe absent de la release, création locale...", "WARN")
        upload_release_asset(token, rel2["id"], build_bootstrapper(clean_version))
    release_assets(token, tag)

    print("=" * 60)
    print("  Terminé.")
    print("=" * 60)


if __name__ == "__main__":
    main()
