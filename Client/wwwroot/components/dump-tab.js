import { loadBaseSheets, loadSheet } from './styles/loader.js';

const baseSheets = await loadBaseSheets();
const ownSheet = await loadSheet('/components/styles/dump-tab.css');

// The dump-tab is a tab that allows the user to dump all server info or clear all server info, used for easy debugging and readability
class DumpTab extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
        this.shadowRoot.adoptedStyleSheets = [...baseSheets, ownSheet];
    }

    connectedCallback() {
        this.shadowRoot.innerHTML = `
            <p class="flow-label">Auth Server - Storage Dump</p>
            <p class="description">
                <strong>Dump all info stored in the Authorization Server</strong> to be viewed here.
                This includes information about users who are currently signed on and what permissions they have been granted,
                as well as all current session information, refresh tokens, consent, etc.
            </p>
            <button class="btn-secondary" id="dump-btn">Dump Auth Info</button>
            <button class="btn-secondary" id="clear-btn">Clear All Info</button>
            <pre id="dump-info"></pre>
        `;

        this.shadowRoot.getElementById('dump-btn')
            .addEventListener('click', this.callDumpEndpoint);
        this.shadowRoot.getElementById('clear-btn')
            .addEventListener('click', this.callClearEndpoint);
    }

    disconnectedCallback() {
        const dumpBtn = this.shadowRoot.getElementById('dump-btn');
        const clearBtn = this.shadowRoot.getElementById('clear-btn');
        if (dumpBtn) {
            dumpBtn.removeEventListener('click', this.callDumpEndpoint);
        }
        if (clearBtn) {
            clearBtn.removeEventListener('click', this.callClearEndpoint);
        }
    }

    callDumpEndpoint = async () => {
        const response = await fetch('/bff/dumpeverything');
        const pre = this.shadowRoot.getElementById('dump-info');
        if (!response.ok && pre) {
            pre.textContent = `Error calling the DumpEverything endpoint.  Status: ${response.status}`;
            return;
        }

        const data = await response.json();
        pre.textContent = JSON.stringify(data, null, 2);
    }

    callClearEndpoint = async () => {
        await fetch('/bff/cleareverything');
        this.callDumpEndpoint();
    }
}

customElements.define('dump-tab', DumpTab);