// CursorMeter for the Zorin OS taskbar.
//
// The CursorMeter program reads the quotas and writes them to $XDG_RUNTIME_DIR/cursormeter/bars.json
// (see PanelFeed.cs). This extension only draws what is in that file: the two pool bars, as the
// Windows edition does, inside the panel that Zorin Taskbar hosts. It has no quota logic.
//
// While it is enabled it creates $XDG_RUNTIME_DIR/cursormeter/panel-extension, so the program stops
// writing the percentages as text next to its tray icon.

import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import GLib from 'gi://GLib';
import St from 'gi://St';

import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as PanelMenu from 'resource:///org/gnome/shell/ui/panelMenu.js';

const FEED_VERSION = 1;
const FEED_NAME = 'bars.json';
const MARKER_NAME = 'panel-extension';
const FALLBACK_PANEL_HEIGHT = 48;
// 100% text size, in CSS pixels: a little under the 10pt of the Zorin panel's clock.
const BASE_TEXT_SIZE = 12.5;
// Space between the two bars when the feed does not say (AppSettings.DefaultBarSpacing).
const DEFAULT_BAR_SPACING = 4;
const LIVENESS_SECONDS = 30;
const DEBOUNCE_MS = 100;

// The app's own palette (Palette.cs); colours the user chose arrive in the feed.
const DEFAULT_COLORS = {
    text: '#ECEAE2',
    muted: '#C4C0B4',
    track: '#303030',
    cursor: '#7DCEA0',
    other: '#E0B15A',
    warn: '#E0B15A',
    hot: '#E06A5C',
};

function clamp(value, low, high) {
    return Math.min(high, Math.max(low, value));
}

function runtimeDir() {
    return GLib.build_filenamev([GLib.get_user_runtime_dir(), 'cursormeter']);
}

class Meter {
    constructor(uuid) {
        this._uuid = uuid;
        this._feed = null;
        this._indicator = null;
        this._debounceId = 0;
        this._livenessId = 0;
        this._renderId = 0;
        this._markerId = 0;
        this._allocationId = 0;
        this._loggedHeight = null;
        this._zorinSettings = null;
        this._zorinSettingIds = [];
        this._heightId = 0;
        this._monitor = null;
        this._monitorId = 0;
    }

    enable() {
        const dir = runtimeDir();
        GLib.mkdir_with_parents(dir, 0o700);
        this._feedFile = Gio.File.new_for_path(GLib.build_filenamev([dir, FEED_NAME]));
        this._markerFile = Gio.File.new_for_path(GLib.build_filenamev([dir, MARKER_NAME]));

        this._indicator = new PanelMenu.Button(0.0, 'CursorMeter', true);
        this._indicator.add_style_class_name('cursormeter-button');
        // Not an St.BoxLayout: Zorin Taskbar forces every St.BoxLayout inside a panel button to the
        // panel's own direction, which would lay the two rows side by side.
        this._box = new St.Widget({
            layout_manager: new Clutter.BoxLayout({orientation: Clutter.Orientation.VERTICAL}),
            style_class: 'cursormeter-box',
            y_align: Clutter.ActorAlign.CENTER,
        });
        this._indicator.add_child(this._box);
        this._indicator.connect('button-press-event', (_actor, event) => {
            if (event.get_button() !== Clutter.BUTTON_PRIMARY)
                return Clutter.EVENT_PROPAGATE;
            this._activateApp();
            return Clutter.EVENT_STOP;
        });
        this._indicator.hide();
        this._allocationId = this._indicator.connect('notify::allocation', () => this._scheduleMarker());
        Main.panel.addToStatusArea(this._uuid, this._indicator, 0, 'right');

        this._heightId = Main.panel.connect('notify::height', () => this._scheduleRender());

        // The size of the Zorin taskbar is a setting; draw again when it changes.
        const schemaId = 'org.gnome.shell.extensions.zorin-taskbar';
        // Gio.Settings aborts the shell for a schema that is not installed, so look it up first.
        if (Gio.SettingsSchemaSource.get_default().lookup(schemaId, true)) {
            this._zorinSettings = new Gio.Settings({schema_id: schemaId});
            this._zorinSettingIds = ['changed::panel-sizes', 'changed::panel-size']
                .map(name => this._zorinSettings.connect(name, () => this._scheduleRender()));
        }

        this._monitor = this._feedFile.monitor_file(Gio.FileMonitorFlags.NONE, null);
        this._monitorId = this._monitor.connect('changed', () => this._scheduleRead());
        this._livenessId = GLib.timeout_add_seconds(GLib.PRIORITY_DEFAULT, LIVENESS_SECONDS, () => {
            this._read();
            return GLib.SOURCE_CONTINUE;
        });

        this._writeMarker();
        this._read();
    }

    disable() {
        if (this._debounceId) {
            GLib.source_remove(this._debounceId);
            this._debounceId = 0;
        }
        if (this._livenessId) {
            GLib.source_remove(this._livenessId);
            this._livenessId = 0;
        }
        if (this._monitor) {
            this._monitor.disconnect(this._monitorId);
            this._monitor.cancel();
            this._monitor = null;
        }
        if (this._renderId) {
            GLib.source_remove(this._renderId);
            this._renderId = 0;
        }
        if (this._markerId) {
            GLib.source_remove(this._markerId);
            this._markerId = 0;
        }
        if (this._allocationId) {
            this._indicator?.disconnect(this._allocationId);
            this._allocationId = 0;
        }
        if (this._heightId) {
            Main.panel.disconnect(this._heightId);
            this._heightId = 0;
        }
        if (this._zorinSettings) {
            this._zorinSettingIds.forEach(id => this._zorinSettings.disconnect(id));
            this._zorinSettingIds = [];
            this._zorinSettings = null;
        }
        this._removeMarker();
        this._indicator?.destroy();
        this._indicator = null;
        this._box = null;
        this._feed = null;
    }

    // The marker also says where the bars are on the screen, so the program can open its summary
    // centred above them (PanelAnchor.cs). Empty until the bars have been placed.
    _writeMarker() {
        let text = '{}';
        try {
            if (this._indicator?.visible) {
                const [x, y] = this._indicator.get_transformed_position();
                const [width, height] = this._indicator.get_transformed_size();
                if (width > 0 && height > 0)
                    text = JSON.stringify({x: Math.round(x), y: Math.round(y), width: Math.round(width), height: Math.round(height)});
            }
            this._markerFile.replace_contents(new TextEncoder().encode(text), null, false,
                Gio.FileCreateFlags.NONE, null);
        } catch (e) {
            console.error(`CursorMeter: could not write the marker: ${e.message}`);
        }
    }

    // The bars move when the taskbar is resized or the text changes size, so keep the marker current.
    _scheduleMarker() {
        if (this._markerId)
            return;
        this._markerId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, 250, () => {
            this._markerId = 0;
            this._writeMarker();
            return GLib.SOURCE_REMOVE;
        });
    }

    _removeMarker() {
        try {
            this._markerFile.delete(null);
        } catch (e) {
            // Already gone.
        }
    }

    _scheduleRead() {
        if (this._debounceId)
            return;
        this._debounceId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, DEBOUNCE_MS, () => {
            this._debounceId = 0;
            this._read();
            return GLib.SOURCE_REMOVE;
        });
    }

    // Zorin Taskbar works out its new geometry when the same setting changes, so wait for it.
    _scheduleRender() {
        if (this._renderId)
            return;
        this._renderId = GLib.timeout_add(GLib.PRIORITY_DEFAULT, 300, () => {
            this._renderId = 0;
            this._render();
            return GLib.SOURCE_REMOVE;
        });
    }

    _read() {
        let feed = null;
        try {
            const [ok, bytes] = this._feedFile.load_contents(null);
            if (ok)
                feed = JSON.parse(new TextDecoder().decode(bytes));
        } catch (e) {
            feed = null;
        }

        // A feed from an app that has since exited (or crashed) must not stay on the panel.
        if (feed && (feed.version !== FEED_VERSION || !GLib.file_test(`/proc/${feed.pid}`, GLib.FileTest.EXISTS)))
            feed = null;

        this._feed = feed;
        this._render();
    }

    _color(name) {
        return this._feed?.colors?.[name] ?? DEFAULT_COLORS[name];
    }

    _render() {
        if (!this._indicator)
            return;

        try {
            this._box.destroy_all_children();
            const feed = this._feed;
            if (!feed) {
                this._indicator.hide();
                return;
            }

            const pools = feed.pools ?? [];
            const rows = Math.max(1, pools.length);

            // Actor sizes are in pixels already multiplied by the UI scale; CSS lengths are not.
            const uiScale = St.ThemeContext.get_for_stage(global.stage).scale_factor;
            const available = this._availableHeight(uiScale);
            const room = Math.max(12 * uiScale, Math.floor((available - 6 * uiScale) / rows));

            // The text size is the user's setting (80% to 110%, as on Windows) over a base close to
            // the panel's own text; only a very low panel makes it smaller.
            const textScale = clamp(feed.textScale ?? 100, 80, 110) / 100;
            const fontCss = Math.min(BASE_TEXT_SIZE * textScale, room / uiScale * 0.7);
            const fontPx = fontCss * uiScale;

            // The rows hug the text and sit in the middle of the panel, however tall it is. The space
            // between them is the user's setting, cut down only when the panel cannot hold it.
            const rowPx = Math.min(room, Math.round(fontPx * 1.3));
            const wanted = Math.round(clamp(feed.barSpacing ?? DEFAULT_BAR_SPACING, 0, 16) * uiScale);
            const fits = rows > 1 ? Math.max(0, Math.floor((available - 4 * uiScale - rows * rowPx) / (rows - 1))) : 0;
            this._box.layout_manager.spacing = Math.min(wanted, fits);
            const size = {uiScale, rowPx, fontCss, fontPx};

            if (available !== this._loggedHeight) {
                this._loggedHeight = available;
                console.log(`CursorMeter: panel height ${available}px, scale ${uiScale}, row ${rowPx}px, text ${fontCss}px`);
            }

            if (pools.length === 0) {
                this._box.add_child(this._label(feed.status ?? feed.detail ?? '', this._color('muted'), size.fontCss, 0, false));
            } else {
                for (const pool of pools)
                    this._box.add_child(this._row(pool, size));
            }

            this._indicator.show();
            this._scheduleMarker();
        } catch (e) {
            console.error(`CursorMeter: could not draw the bars: ${e.message}`);
        }
    }

    // Zorin Taskbar keeps the real panel geometry on the panel's delegate. Main.panel's own height
    // is 0 until the panel is allocated, which is when the extension is enabled.
    _availableHeight(uiScale) {
        const inner = Main.panel._delegate?.geom?.innerSize;
        if (Number.isFinite(inner) && inner > 0)
            return inner;
        return Main.panel.get_height() || FALLBACK_PANEL_HEIGHT * uiScale;
    }

    _label(text, color, fontCss, widthPx, right) {
        const label = new St.Label({
            text,
            y_align: Clutter.ActorAlign.CENTER,
            style: `color: ${color}; font-size: ${fontCss}px;${right ? ' text-align: right;' : ''}`,
        });
        if (widthPx > 0)
            label.set_width(widthPx);
        return label;
    }

    _row(pool, size) {
        const {uiScale, rowPx, fontCss, fontPx} = size;
        const tone = pool.tone;
        const poolColor = this._color(pool.id === 'cursor' ? 'cursor' : 'other');
        const fillColor = tone === 'hot' ? this._color('hot') : tone === 'warn' ? this._color('warn') : poolColor;
        const textColor = tone === 'hot' ? fillColor : this._color('text');

        const barWidth = Math.round(fontPx * 9);
        const thickness = clamp(Math.round(fontPx * 0.5), 4 * uiScale, 10 * uiScale);
        const radiusCss = Math.ceil(thickness / uiScale / 2);

        const row = new St.Widget({
            layout_manager: new Clutter.BoxLayout({spacing: Math.round(8 * uiScale)}),
            height: rowPx,
        });
        row.add_child(this._label(pool.label, textColor, fontCss, Math.round(fontPx * 8.2), false));

        // A horizontal box lays the fill out from the left edge of the track.
        const track = new St.Widget({
            layout_manager: new Clutter.BoxLayout(),
            y_align: Clutter.ActorAlign.CENTER,
            width: barWidth,
            height: thickness,
            style: `background-color: ${this._color('track')}; border-radius: ${radiusCss}px;`,
        });
        const filled = Math.round(barWidth * clamp(pool.fill ?? 0, 0, 1));
        if (filled > 0) {
            track.add_child(new St.Widget({
                width: filled,
                height: thickness,
                style: `background-color: ${fillColor}; border-radius: ${radiusCss}px;`,
            }));
        }
        row.add_child(track);

        row.add_child(this._label(pool.text, textColor, fontCss, Math.round(fontPx * 3.6), true));
        return row;
    }

    // The app's StatusNotifierItem opens the summary on Activate, and its bus name carries its pid.
    _activateApp() {
        const pid = this._feed?.pid;
        if (!pid)
            return;
        Gio.DBus.session.call(
            `org.kde.StatusNotifierItem-${pid}-1`,
            '/StatusNotifierItem',
            'org.kde.StatusNotifierItem',
            'Activate',
            new GLib.Variant('(ii)', [0, 0]),
            null,
            Gio.DBusCallFlags.NONE,
            -1,
            null,
            (connection, result) => {
                try {
                    connection.call_finish(result);
                } catch (e) {
                    console.error(`CursorMeter: could not open the summary: ${e.message}`);
                }
            });
    }
}

export default class CursorMeterExtension extends Extension {
    enable() {
        this._meter = new Meter(this.uuid);
        this._meter.enable();
    }

    disable() {
        this._meter?.disable();
        this._meter = null;
    }
}
