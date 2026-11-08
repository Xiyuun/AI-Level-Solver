import { Component, type ErrorInfo, type ReactNode } from 'react'

// 渲染错误边界：任何子树渲染抛异常时给出可读提示 + 刷新按钮，而不是整页白屏
// 背景：门1 报告多了一个派生布尔字段，前端把它当检查项访问 issues.length，
// 未捕获的渲染异常会让 React 卸载整棵根节点——用户只能看到白页，且不知道原因。
export class ErrorBoundary extends Component<{ children: ReactNode }, { message: string | null }> {
  state = { message: null as string | null }

  static getDerivedStateFromError(error: Error) {
    return { message: error.message || String(error) }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('页面渲染异常：', error, info)
  }

  render() {
    if (this.state.message !== null) {
      return (
        <div className="error" style={{ margin: 24, lineHeight: 1.8 }}>
          页面渲染出错：{this.state.message}
          <br />
          这是前端渲染异常（不是数据丢失）。请刷新页面；若反复出现，把这段错误信息反馈给开发者。
          <br />
          <button onClick={() => window.location.reload()}>刷新页面</button>
        </div>
      )
    }
    return this.props.children
  }
}
