// We should import these JS files to ensure they are parsed before this file (since we use them as a part of this component)
// For our app, since the user has to interact before they would be rendered, we wouldn't really need this, but it is good practice
// And allows us to not include a script tag in our page HTML for any component not explicitly loaded there
import "./tab-bar.js";
import "./oidc-flow-tab.js";
import "./dump-tab.js";
import { loadBaseSheets } from './styles/loader.js';

const baseSheets = await loadBaseSheets();

// The app-shell is our wrapper class that lays out our web components
class AppShell extends HTMLElement {
    constructor() {
        super();
        this.attachShadow({ mode: 'open' });
        this.shadowRoot.adoptedStyleSheets = [...baseSheets];
        this._activeTab = 'combined';
    }

    connectedCallback() {
        this.render();
        this.addEventListener('tab-change', (e) => {
            this._activeTab = e.detail.tab;
            this.render();
        })
    }

    render() {
        this.shadowRoot.innerHTML = `
            <style>
                :host { display: block; }
                tab-bar { display: block; }
                .panel { display: none; }
                .panel.active {display: block; }
            </style>
            <tab-bar active="${this._activeTab}"></tab-bar>
            <div class="panel ${this._activeTab === 'combined' ? 'active' : ''}">
                <oidc-flow-tab login-type="full"></oidc-flow-tab>
            </div>
            <div class="panel ${this._activeTab === 'oidc' ? 'active' : ''}">
                <oidc-flow-tab login-type="identity"></oidc-flow-tab>
            </div>
            <div class="panel ${this._activeTab === "dump" ? 'active' : ''}">
                <dump-tab></dump-tab>
            </div>
        `;
    }
}

customElements.define('app-shell', AppShell);