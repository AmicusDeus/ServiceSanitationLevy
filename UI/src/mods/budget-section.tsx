import { Component } from "react";

// Shared error boundary: if an injected component ever throws, render nothing instead of breaking the
// host panel. Used by the budget/section/info-panel injections in index.tsx.
export class Safe extends Component<{ children: any }, { err: boolean }> {
    constructor(props: any) {
        super(props);
        this.state = { err: false };
    }
    static getDerivedStateFromError() {
        return { err: true };
    }
    render() {
        return this.state.err ? null : this.props.children;
    }
}
