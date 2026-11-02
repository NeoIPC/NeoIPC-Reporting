#!/bin/sh
# Usage: check-tex-packages <tlmgr> <package>...
#
# Checks that every TeX package named, and every package it depends on, is installed, and fails otherwise, naming
# the missing ones. `tlmgr install` does not fail when installing a package does, as when its download breaks off:
# its action_install (tlmgr.pl, texlive.infra r79982) logs the failure to tlmgr.log and leaves its own return value
# at success, so a build would pass and ship an image whose renders fail. The check walks the dependency closure of
# the named packages through the installed package database and stops at the first round that names a package not
# installed: `tlmgr info --only-installed` warns "package <name> not locally!" for each one and exits 1. A binary
# dependency, `<name>.ARCH`, is installed as `<name>.x86_64-linux`, and a Windows-only one, `<name>.windows`, not at
# all.
set -eu
tlmgr=$1
shift
todo="$*"
seen=" $* "
while [ -n "$todo" ]; do
    # $todo is split into package names on purpose.
    deps=$("$tlmgr" info --only-installed --data depends $todo) || {
        echo "tlmgr could not confirm the installed TeX packages; its messages above say why." >&2
        exit 1
    }
    todo=
    for dep in $(printf '%s\n' "$deps" | tr ':' ' '); do
        case $dep in
            *.windows) continue ;;
            *.ARCH) dep="${dep%.ARCH}.x86_64-linux" ;;
        esac
        case $seen in
            *" $dep "*) continue ;;
        esac
        seen="$seen$dep "
        todo="$todo $dep"
    done
done
echo "All $(echo $seen | wc -w) TeX packages, the requested ones and their dependencies, are installed."
