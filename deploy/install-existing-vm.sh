#!/usr/bin/env bash
# Run only after reviewing the VM and existing nginx vhost. Does not create cloud resources.
set -euo pipefail
upload="${1:?Upload directory required}"
vhost="${2:?Existing nginx vhost path required}"
if [ "$vhost" != /etc/nginx/sites-available/donbot ]; then
    echo "This reviewed installer expects the existing donbot vhost" >&2; exit 1
fi
if [ -e /opt/diplayer/DIPlayer.Server ]; then
    echo "DIPlayer already installed; review an update separately" >&2; exit 1
fi
if ! id diplayer >/dev/null 2>&1; then
    useradd --system --home /var/lib/diplayer --shell /usr/sbin/nologin diplayer
fi
install -d -o root -g root -m 0755 /opt/diplayer
install -d -o root -g diplayer -m 0750 /etc/diplayer
install -d -o diplayer -g diplayer -m 0700 /var/lib/diplayer
tar -xzf "$upload/server.tar.gz" -C /opt/diplayer
chown -R root:root /opt/diplayer
chmod 0755 /opt/diplayer/DIPlayer.Server
install -o root -g root -m 0644 "$upload/diplayer.service" /etc/systemd/system/diplayer.service
install -o root -g root -m 0644 "$upload/nginx-location.conf" /etc/nginx/snippets/diplayer.conf
cp -p "$vhost" "$vhost.before-diplayer-20261016"
python3 - "$vhost" <<'PY'
import pathlib,sys
p=pathlib.Path(sys.argv[1]);s=p.read_text()
marker='    location / {'
if 'snippets/diplayer.conf' in s or s.count(marker)!=1:
    raise SystemExit('Unexpected vhost structure; refusing to edit')
p.write_text(s.replace(marker,'    include /etc/nginx/snippets/diplayer.conf;\n'+marker,1))
PY
if ! nginx -t; then
    cp -p "$vhost.before-diplayer-20261016" "$vhost"
    echo 'nginx config restored; no reload performed' >&2
    exit 1
fi
systemctl daemon-reload
echo 'Files installed. Supply secrets.env before enabling the service.'
