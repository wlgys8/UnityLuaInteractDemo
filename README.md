# Unity中C#与Lua的交互

Lua是一种嵌入式脚本语言，可以方便的与c/c++进行相互调用。但是Unity中主要是用c#进行开发的，因此在Unity中使用Lua通常有以下两种方案:

- 使用c#实现一个lua虚拟机
- 基于原生的c lua api做一个封装，让c#调用

从性能上考虑，当前主流方案都是第二种。

基于第二种方案实现的框架目前主要有xLua,sLua,uLua,NLua(+KeraLua)。在这些方案中，都能找到一个相关的类，封装了c#对lua c api的调用。例如在xlua中是`XLua.LuaDLL.Lua`这个类，在slua中是`SLua.LuaDll`这个类。

所以在Unity里执行Lua是以c作为中间媒介的:
```
C# <=> C <=> Lua
```


Lua与宿主语言(这里以c#为例)最基础的两种交互模式即:

- c#执行lua代码
- lua执行c#静态/成员函数

这种交互是通过一个栈结构进行的。

为了更清楚的理解和阐述这个交互过程，本文将使用`KeraLua`来写一些用例代码。为什么使用KeraLua呢？ 因为相比较xLua、sLua、uLua，KeraLua是一个纯粹的c#对lua c api的封装，没有多余东西。

KeraLua的Git项目=>(https://github.com/NLua/KeraLua)

Lua的所有的C API可以在官方手册中看到 => [Lua Manual 5.4](http://www.lua.org/manual/5.4/)

(PS: 我只make了KeraLua了OSX库，其他平台的请自行编译)

本文中将会阐述的交互用例罗列如下:

- 初始化lua栈
- c#执行lua代码
- c#调用lua全局函数
- lua注册并调用c#静态函数
- lua注册c#类型
  - 注入c#类的静态函数
  - 注入c#类的构造函数
  - 注入c#类成员函数
- GC管理
- c#引用lua中的临时函数
- 无法解决的循环引用问题


# 1. 栈的结构索引

Lua与宿主语言是通过栈进行交互的。在c中通常以`lua_State* L`的形式表示指向栈的一个指针，在c#中以`System.IntPtr L`的形式存在。

栈的元素用过index进行索引。以负数表示从顶向底索引，以正数表示由底向顶索引。如下图所示:

<img src="http://4.bp.blogspot.com/-dvT7Q3w1YBk/Tkxp7AvT7vI/AAAAAAAAAB4/VH1s8wVYgVY/s320/luaStack1.png">

因此-1表示表示栈顶元素，1表示栈底元素。在许多api中，都需要通过索引来读取栈中数据、或者向栈中指定位置填充数据。

# 2. 创建Lua栈

```csharp
var L = Lua.luaL_newstate();
Lua.lua_close(L);
```
- `luaL_newstate`可以创建一个虚拟栈，返回的L为System.IntPtr类型，代表了栈的指针
- `lua_close`用于关闭释放栈

这个创建的栈，将用作c#与lua进行数据交互

# 3. c#执行lua代码

这里将分三个步骤:

- 加载lua代码到vm中，对应api - [`luaL_loadbuffer`](https://www.lua.org/manual/5.4/manual.html#luaL_loadbuffer)
  - luaL_loadbuffer会同时在栈上压入代码块的指针
- 执行lua代码，对应api - [`lua_pcall`](https://www.lua.org/manual/5.3/manual.html#lua_pcall)
  - lua_pcall会从栈上依次弹出{nargs}个数据作为函数参数，再弹出函数进行执行，并将结果压入栈
- 如果lua代码有返回值，那么通过`lua_toXXX`相关api从栈上获取结果

完整的代码如下:

```csharp
private bool DoLuaCode(System.IntPtr L,string luaCode){
    //加载lua代码
    if(Lua.luaL_loadbuffer(L,luaCode,"") == 0){
        //执行栈顶的函数
        if(Lua.lua_pcall(L,0,1,0) == 0){
            //函数执行完成后，返回值会依次依次押入栈
            return true;
        }else{
            Debug.LogError("pcall failed!");
            return false;
        }
    }else{
        Debug.LogError("load buffer failed");
        return false;
    }
}

```

假如我们有一段lua代码:

```lua
return 'hello, i am from lua'
```

这段lua仅仅返回一段字符串，那么利用`DoLuaCode`去执行就是:

```csharp
//lua代码
string luaCode = @"return 'hello, i am from lua'";
if(DoLuaCode(L,luaCode)){
    Debug.Log(Lua.lua_tostring(L,-1));
    //lua_toXXX不会出栈，需要lua_pop才能出栈
    Lua.lua_pop(L,1);
}
```

- 由于此处lua代码返回的是字符串，因此使用`lua_tostring(L,-1)`来将栈顶的元素转为字符串并返回，相应的我们还能看到有`lua_tonumber`,`lua_toboolean`等等.


# 4. c#调用lua全局函数

接下来的例子将说明一下c#端如何执行lua中的全局函数。

假设现在我们有一段lua代码如下:

```lua
function addSub(a,b)
    return a + b, a-b;
end
```

通过DoLuaCode来运行以上的lua代码，就得到了一个全局的addSub函数，这个函数会返回a,b相加和相减的结果。

为了在c#端执行以上的lua函数，需要按以下步骤进行:

- 将全局函数压入栈中, 对应api - [lua_getglobal](https://www.lua.org/manual/5.4/manual.html#lua_getglobal)
- 将函数所需的参数依次压入栈中，对应api - [lua_pushnumber](https://www.lua.org/manual/5.4/manual.html#lua_pushnumber)
- 执行栈中函数,对应api - lua_pcall
- 获取函数返回结果，对应api - lua_tonumber

完整c#代码如下:

```csharp
//从全局表里读取addSub函数，并压入栈
Lua.lua_getglobal(L,"addSub"); 
//压入参数a
Lua.lua_pushnumber(L,101); 
//压入参数b
Lua.lua_pushnumber(L,202); 
//2个参数,2个返回值
Lua.lua_pcall(L,2,2,0); 
//pcall会让参数和函数指针都出栈
//pcall执行完毕后，会将结果压入栈
Debug.Log(Lua.lua_tonumber(L,-2));
Debug.Log(Lua.lua_tonumber(L,-1));
Lua.lua_pop(L,2);
```

# 5. lua注册并调用c#静态函数

首先，想要被Lua调用的c#函数，都必须满足以下的格式:

```csharp
public delegate int LuaCSFunction(System.IntPtr luaState);
```

同时需要加上特性:

```csharp
MonoPInvokeCallback(typeof(LuaCSFunction))
```

我们可以通过以下方式，将一个LuaCSFunction注册到lua中:

```csharp

static void RegisterCSFunctionGlobal(System.IntPtr L,string funcName,LuaCSFunction func){
    //将LuaCSFunction压入栈中
    Lua.lua_pushcfunction(L,func);
    //lua_setglobal会弹出栈顶元素，并按给定的名字作为key将其加入到全局表
    Lua.lua_setglobal(L,funcName);
}
```

那么，当我们在lua中执行c#注册的函数时，其交互过程如下:

- LuaVM会临时分配一个局部栈结构(这里要区分开始通过luaL_newstate创建的全局栈，两者是独立的)
- LuaVM会将lua侧的函数参数压入这个临时栈，然后将栈指针传给LuaCSFunction
- LuaCSFunction在实现上需要从这个栈中读取lua侧压入的参数，然后执行真正的相关逻辑，并将最终结果压入栈中
- LuaCSFunction需要返回一个int值，表示往栈中压入了多少个返回值
- Lua从栈中获取C#侧压入的0/1/多个返回值

官方说明文档可以参考 - [Calling C from Lua ](https://www.lua.org/pil/26.html)


接下来要将演示如何将一个c#静态函数Print注入到lua中，实现lua中调用c#端的日志输出功能。

我们定义一个c#静态函数如下:

```csharp
[MonoPInvokeCallback(typeof(LuaCSFunction))]
private static int Print(System.IntPtr localL){
    //获取栈中元素个数
    var count = Lua.lua_gettop(localL);
    System.Text.StringBuilder s = new System.Text.StringBuilder();
    for(var i = 1; i <= count; i ++){
        //依次读取print的每个参数，合并成一个string
        s.Append(Lua.lua_tostring(localL,i));
        s.Append(' ');
    }
    Debug.Log(s);
    //print函数没有返回值
    return 0;
}
```

- lua_gettop 可以获取栈中的元素个数，此处代表了lua端压入栈中的函数参数个数

然后我们通过以下方式将这个c#侧的`Print`注册到lua中，命名为`print`。

```csharp
//将LuaCSFunction压入栈中
Lua.lua_pushcfunction(L,Print);
//lua_setglobal会弹出栈顶元素，并按给定的名字作为key将其加入到全局表
Lua.lua_setglobal(L,"print");
```

接下来我们执行以下的lua代码:

```lua
print('hello','csharp')
```

就能看到编辑器中输出

```
hello csharp
```

# 6. lua注册c#类型

通常我们使用lua中的table来模拟c#中的类。一般类的注册思路如下:

- 在lua中创建一个与c#类同名的表
- 将c#类的静态函数都注册到lua的这个同名表里

下面演示一下如何将Unity中的Debug类注册到lua中:

```csharp
Lua.lua_createtable(L,0,1);
Lua.lua_setglobal(L,"Debug");
```

其实很简单:

- lua_createtable会创建一个table，压入栈顶
- lua_setglobal会弹出栈顶元素，并将其加到全局表里

这样我们在lua里就有了一个名为Debug的表可供全局访问。但目前这个表是空空如也的，我们还需要为其添加静态函数。(tips:实际上完整的设计中，还需要为class table设置metatable，增加一些限制性，但这里先不表)


## 6.1 注入类的静态函数


首先我们定义一个符合LuaCSFunction形式的c#函数如下:

```csharp
[MonoPInvokeCallback(typeof(LuaCSFunction))]
private static int Debug_Log(System.IntPtr L){
    string msg = Lua.lua_tostring(L,1);
    Debug.Log(msg);
    return 0;
}
```
这个c#函数是对`Debug.Log`的一个封装。

然后可以通过以下方式将这个c#函数注册到lua中的Debug表中:

```csharp
Lua.lua_createtable(L,0,1);

//往栈中压入字符串'Log'
Lua.lua_pushstring(L,"Log");
//往栈中压入函数Debug_Log
Lua.lua_pushcfunction(L,Debug_Log);
//从栈中弹出一个元素作为key，再弹出一个元素作为value，作为pair赋值到index指定的table
Lua.lua_settable(L,1);

Lua.lua_setglobal(L,"Debug");
```

这里的关键是`lua_settable`这个函数，它等于执行了一个`table[key]=value`的操作。

以上就完成了`Debug.Log`这个函数在Lua中的注册.

我们运行以下的lua代码能在编辑器中看到正确输出:

```lua
Debug.Log('call debug.log from lua')
```

tips: 在实际的解决方案中，人们一般通过反射技术遍历一个c#类的所有静态函数，自动生成以上形式的模板代码完成注册，就不用手写了。

## 6.2 注入类的构造函数

考虑我们有一个c#的类`GameObject`，我们希望将这个类注册到lua中，并在lua中执行以下代码:

```lua
local go = GameObject('LuaGO')
go:SetActive(false)
```

按照前面的方式，我们已经可以将GameObject作为一个table注册到lua中，并注册其所有静态函数。但为了实现以上的代码调用，还需要注册构造函数到lua。

在lua中，要让一个table可以像函数一样被调用，需要为其设置metatable，并在其中增加一个`__call`函数.

这样当我们在lua中执行`GameObject()`时，就会触发其metatable中的`__call`函数.

完整的代码如下:

```csharp
//local GameObject = {}
//L.push(GameObject)
Lua.lua_createtable(L,0,1);

//local classMeta = {}
//L.push(classMeta)
Lua.lua_createtable(L,0,1); 

//classMeta.__call = GameObject_Constructor
Lua.lua_pushstring(L,"__call");
Lua.lua_pushcfunction(L,GameObject_Constructor);
Lua.lua_settable(L,-3);

//会将栈顶元素弹出，作为metatable赋给指定的索引位置的元素
Lua.lua_setmetatable(L,-2);
//将栈顶元素弹出，设为全局变量
Lua.lua_setglobal(L,"GameObject");
```

在以上代码中，我们依次往栈中压入两个表，一个作为`GameObject Class`对象,一个作为其`metatable`。

接下来通过lua_pushXXX和lua_settable的方式为metatable设置了`__call`函数。

然后通过lua_setmetatable为`GameObject class`设置好metatable，最后导出到lua全局表。

接下来看一下__call函数在c#端的实现:

```csharp
[MonoPInvokeCallback(typeof(LuaCSFunction))]
private static int GameObject_Constructor(System.IntPtr L){
    string name = Lua.lua_tostring(L,1);
    var go = new GameObject(name);
    //创建一个userdata，代表gameObject实例
    var udptr = Lua.lua_newuserdata(L,(uint)4);
    return 1;
}
```

注意到我们使用了一个新的api - `lua_newuserdata`.

构造函数需要返回一个c#对象到lua中，实际上我们并不能真正将c#对象返回到lua，因此这里使用了userdata类型的lua对象作为c#对象在lua中的`替身`.

userdata是lua中的一种类型，其代表了在宿主语言中分配出来的一块内存区域，但生命周期却是交给lua的gc来管理的。我们同样可以为userdata变量设置metatable,以此为其增加各种方法、属性.

## 6.3 注入c#类成员函数

在6.2中，虽然通过以下的代码可以完成GameObject构造函数的调用:

```lua
local go = GameObject('GO')
print(type(go)) --输出 userdata
```
但go还并不具备任何成员函数。我们将要为go设置metatable，以赋予其相关的成员函数。

```csharp
//创建一个metatable并放到lua注册表中，同时压入栈顶
//local metatable = {}
//register["GameObject"] = metatable
Lua.luaL_newmetatable(L,"GameObject");

//local __index = {}
Lua.lua_pushstring(L,"__index");
Lua.lua_createtable(L,0,1);

//__index.SetActive = GameObject_SetActive
Lua.lua_pushstring(L,"SetActive");
Lua.lua_pushcfunction(L,GameObject_SetActive);
Lua.lua_settable(L,-3);

//metatable.__index = __index
Lua.lua_settable(L,-3);

//弹出metatable
Lua.lua_pop(L,1);
```

以上代码等效于创建了以下一个metatable表:

```lua
{
    __index = {
        SetActive = GameObject_SetActive
    }
}
```
并将这个表放到lua的注册表中,key为`'GameObject'`。我们将为所有的GameObject实例`替身`使用这个metatable。

将GameObject_Constructor修改如下:

```csharp
[MonoPInvokeCallback(typeof(LuaCSFunction))]
private static int GameObject_Constructor(System.IntPtr L){
    string name = Lua.lua_tostring(L,1);
    var go = new GameObject(name);
    var udptr = Lua.lua_newuserdata(L,(uint)4);
    //为userdata设置metatable
    Lua.luaL_setmetatable(L,"GameObject");
    _objectCache.Add(udptr,go);
    return 1;
}
```
这里通过`luaL_setmetatable`这个api，为新创建出来的userdata设置了`'GameObject'`这个metatable。这样我们就为这个替身赋予了SetActive这个成员函数。

```lua
local go = GameObject('GO')
print(type(go)) -- userdata类型
go:SetActive(false); -- 会从metatable的__index这个表中，找到SetActive这个方法进行调用
```

注意到前面我们使用了`_objectCache`将`userdata`和`go`的映射缓存起来，这是因为后续lua中执行userdata上的成员函数时，我们需要通过这个cache找到userdata在c#中对应的实例。

例如c#端的SetActive封装函数如下:

```csharp
[MonoPInvokeCallback(typeof(LuaCSFunction))]
private static int GameObject_SetActive(System.IntPtr L){
    //第一个参数是userdata
    var udptr = Lua.lua_touserdata(L,1);
    //第二个参数材质active
    var active = Lua.lua_toboolean(L,2) != 0;
    var go = _objectCache[udptr] as GameObject;
    go.SetActive(active);
    return 0;
}
```

# 7. GC管理

在part 6中我们在c#端通过objectCache缓存了userdata和go。 同时在lua端通过`GameObject()`返回了一个userdata对象代表GameObject实例。 

userdata的生命周期是交给lua vm来管理的，因此假如我们在lua中没有引用住这个go对象，那么很快就会被gc回收掉。 这样我们在c#端objectCache中缓存的userdata就会成为传说中的野指针，同时造成内存泄露。

为了解决这个问题，需要在c#端监听lua中对象的gc情况,当userdata被lua vm gc回收时，我们同步将其从objectCache中移除.

好在lua的metatable中提供了__gc这个函数，当对象被gc回收时会触发。 因此我们只要在对象的metatable上额外注册__gc函数就可以了:

```csharp
//metatable.__gc = gc -- 设置gc函数
Lua.lua_pushstring(L,"__gc");
Lua.lua_pushcfunction(L,gc);
Lua.lua_settable(L,-3);
```

c#端的gc函数实现如下:

```csharp
[MonoPInvokeCallback(typeof(LuaCSFunction))]
private static int gc(System.IntPtr L){
    var udptr = Lua.lua_touserdata(L,1);
    if(_objectCache.Remove(udptr)){
        // Debug.Log("gc called for userdata : " + udptr);
    }else{
        Debug.LogError("cache missing:" + udptr);
    }
    return 0;
}

```

# 8. c#引用lua中的临时函数

某些情况下，我们需要在c#中引用住lua中传递的临时函数。例如实现一些回调函数接口时。考虑以下用例:

```lua
local callback = function()

end
EventManager.Register(callback)
```

这里我们往c#端的EventManager中注册了一个lua函数作为callback。在c#端需要对其进行引用，并在合适的时机执行这个callback。（否则callback在luavm中因为不存在引用，会被gc回收调)

c#端，EventManager.Register实现如下:

```csharp
[MonoPInvokeCallback(typeof(LuaCSFunction))]
private static int EventManager_Register(System.IntPtr L){
    //即LuaRegistery[reference] = luaCallback
    var reference = Lua.luaL_ref(L,(int)LUA_REGISTRY.Index);
    var luaFunc = new LuaFunction(_globalL,reference);
    EventManager.Register((value)=>{
        luaFunc.PCall(value);
    });
    return 0;
}
```

这里使用了`luaL_ref`这个api，它会将栈顶元素添加到lua的注册表中(这样就不会被luavm gc回收)。`luaL_ref`会返回一个int类型的reference，用于后续去注册表中重新获取该元素.

既然我们使用`luaL_ref`引用住了lua中的一个临时变量，那么就需要在恰当的时机释放这个临时变量，否则lua端会造成内存泄露。

在本用例里,这个lua function的生命周期应当跟c#注册到EventManager中的Delegate对象保持一致。

因此我们新建了LuaFunction这个类，来维护lua中这个reference的生命周期:

```csharp
public class LuaFunction{

    private int _reference;
    private System.IntPtr _L;
    public LuaFunction(System.IntPtr L, int reference){
        _reference = reference;
        _L = L;
    }

    public void PCall(int value){
        //根据reference从registery中取到lua callback，放到栈顶
        Lua.lua_rawgeti(_L,(int)LUA_REGISTRY.Index,_reference);
        //压入参数
        Lua.lua_pushinteger(_L,value);
        //执行lua callback
        Lua.lua_pcall(_L,1,0,0);
    }

    ~LuaFunction(){
        Lua.luaL_unref(_L,(int)LUA_REGISTRY.Index,_reference);
        Debug.Log("LuaFunction gc in c#:" + _reference);
    }
}
```

可以看到，LuaFunction实现了析构函数。即当LuaFunction这个对象在c#端被GC回收时，我们同步释放其所维护的lua reference. 


所以这里c#端的引用关系是:

EventManager->Delegate->LuaFunction

这样就成功在c#端完成了对lua端对象的引用和生命周期维护。

# 9. 无法解决的循环引用

在part7中，c#将自己的一个临时对象生命周期委托给lua的gc管理.

在part8中，lua将自己的一个临时对象生命周期委托给c#的gc管理.

但这种设计并不是完美无缺的，以下这种情形将会导致循环引用，并使得两边的gc都无法释放对象:

```lua

local csObj = CSObject()
csObj:AddCallback(function()
    csObj:DoSomething()
end)
```

以上这个用例的引用链如下:

lua端: LuaRegistery->luaCallback->csObj

c#端: objectCache->csObj->Delegate->LuaFunction->luaCallback

lua端依赖c#这边的gc释放LuaFunction，从而对luaCallback进行解引用，才能触发csObj(userData)的gc

但c#端又依赖lua这边对csObj(userData)进行gc回收，才能从objectCache中移除csObj.

这就造成了死锁，两边都无法进行回收，并且两边都已完全失去了对象的访问能力(因为lua代码中无法访问LuaRegistery，同样c#端通常不会将objectCache暴露给上层使用者)。

目前似乎没有确切的，自动化的解决方案(ps:我只用过slua，里面是存在这个问题的)


# 10. 结束

到这里为止，c#与lua的几种交互情形基本上已经罗列清楚了。Unity中的各种lua解决方案，基本上是针对以上的交互情形，提供了更高性能的、更少GC的高级封装，并且通过自动化工具生成模板代码，将c#中的类、函数注入到lua中。


