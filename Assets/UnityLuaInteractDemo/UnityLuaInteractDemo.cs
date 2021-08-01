using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using LuaCSFunction = KeraLua.LuaFunction;
using Lua = KeraLua.NativeMethods;
using LUA_REGISTRY = KeraLua.LuaRegistry;

#pragma warning disable 414
    public class MonoPInvokeCallbackAttribute : System.Attribute
    {
        private System.Type type;
        public MonoPInvokeCallbackAttribute(System.Type t) { type = t; }
    }
#pragma warning restore 414


public class UnityLuaInteractDemo : MonoBehaviour
{

    private static System.IntPtr _globalL;

    void Start()
    {
        //创建一个lua栈
        var L = Lua.luaL_newstate();
        _globalL = L;
        Lua.luaL_openlibs(L);
        DoLuaCodeTest(L);
        CallLuaFunc(L);
        CallLuaGlobalFunc(L);
        LuaCallStaticCSFunc(L);
        RegisterDebugClassToLua(L);
        CallDebugLog(L);
        RegisterGameObjectToLua(L);
        CallGameObjectSetActiveInLua(L);

        RegisterEventManagerToLua(L);
        DoEventManagerLuaCode(L);
        
    }

    void OnDestroy(){
        if(_globalL != System.IntPtr.Zero){
            Lua.lua_close(_globalL);
            _globalL = System.IntPtr.Zero;
        }
    }

    void Update(){
        if(Input.GetKeyDown(KeyCode.Space)){
            EventManager.Dispatch(Time.frameCount);
        }

        if(Input.GetKeyDown(KeyCode.C)){
            EventManager.Clear();
        }
    }
    
    private bool DoLuaCode(System.IntPtr L,string luaCode){
        //加载lua代码
        if(Lua.luaL_loadbuffer(L,luaCode,"") == 0){
            //执行栈顶的函数,参数分别为 int nArgs, int nResults, int errfunc
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

    private void DoLuaCodeTest(System.IntPtr L){
        //lua代码
        string luaCode = @"return 'hello, i am from lua'";
        if(DoLuaCode(L,luaCode)){
            //函数执行完成后，回将返回值依次押入栈中，通过lua_tonumber来获取栈中元素
            Debug.Log(Lua.lua_tostring(L,-1));
            //lua_toXXX不会出栈，需要lua_pop才能出栈
            Lua.lua_pop(L,1);
        }
    }

    /// <summary>
    /// c#调用lua函数
    /// </summary>
    private void CallLuaFunc(System.IntPtr L){
        //lua代码
        string luaCode = 
        @"return function(a,b)
            return a + b, a-b;
        end";
        if(DoLuaCode(L,luaCode)){
            //现在栈顶是luaCode返回的匿名函数
            Lua.lua_pushnumber(L,101); //参数a
            Lua.lua_pushnumber(L,202); //参数b
            Lua.lua_pcall(L,2,2,0);
            //执行完毕后，会将结果压入栈
            //获取结果
            Debug.Log(Lua.lua_tonumber(L,-2));
            Debug.Log(Lua.lua_tonumber(L,-1));
            Lua.lua_pop(L,2);
        }
    }

    private void CallLuaGlobalFunc(System.IntPtr L){
        //lua代码
        string luaCode = 
        @"function addSub(a,b)
            return a + b, a-b;
        end";
        if(DoLuaCode(L,luaCode)){
            //从全局表里读取addSub函数，并压入栈
            Lua.lua_getglobal(L,"addSub"); 
            //压入参数a
            Lua.lua_pushnumber(L,101); 
            //压入参数b
            Lua.lua_pushnumber(L,202); 
            //2个参数,2个返回值
            Lua.lua_pcall(L,2,2,0); 
            //执行完毕后，会将结果压入栈
            //获取结果
            Debug.Log(Lua.lua_tonumber(L,-2));
            Debug.Log(Lua.lua_tonumber(L,-1));
            Lua.lua_pop(L,2);
        }
    }

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

    private static void RegisterCSFunction(System.IntPtr L,LuaCSFunction function,string globalLuaName){
        //将LuaCSFunction压入栈中
        Lua.lua_pushcfunction(L,function);
        //lua_setglobal会弹出栈顶元素，并按给定的名字作为key将其加入到全局表
        Lua.lua_setglobal(L,globalLuaName);
    }


    /// <summary>
    /// Lua调用CSharp函数
    /// </summary>
    private void LuaCallStaticCSFunc(System.IntPtr L){
        RegisterCSFunction(L,Print,"print");
        string luaCode = 
        @"
            print('hello','csharp')
        ";
        DoLuaCode(L,luaCode);
    }



    [MonoPInvokeCallback(typeof(LuaCSFunction))]
    private static int Debug_Log(System.IntPtr L){
        string msg = Lua.lua_tostring(L,1);
        Debug.Log(msg);
        return 0;
    }


    /// <summary>
    /// 注册Debug类型到lua中，并实现静态函数Debug.Log
    /// </summary>
    /// <param name="L"></param>
    private void RegisterDebugClassToLua(System.IntPtr L){
        Lua.lua_createtable(L,0,1);

        Lua.lua_pushstring(L,"Log");
        Lua.lua_pushcfunction(L,Debug_Log);
        Lua.lua_settable(L,-3);

        Lua.lua_setglobal(L,"Debug");
    }

    /// <summary>
    /// 在Lua中运行Debug.Log()进行测试
    /// </summary>
    private void CallDebugLog(System.IntPtr L){
        string lua = @"Debug.Log('call debug.log from lua')";
        DoLuaCode(L,lua);
    }


    private static Dictionary<System.IntPtr,object> _objectCache = new Dictionary<System.IntPtr, object>();


    [MonoPInvokeCallback(typeof(LuaCSFunction))]
    private static int GameObject_Constructor(System.IntPtr L){
        string name = Lua.lua_tostring(L,1);
        var go = new GameObject(name);
        //创建一个userdata，代表gameObject实例
        var udptr = Lua.lua_newuserdata(L,(uint)4);
        //设置userdata的metatable
        Lua.luaL_setmetatable(L,"GameObject");

        _objectCache.Add(udptr,go);

        return 1;
    }

    [MonoPInvokeCallback(typeof(LuaCSFunction))]
    private static int GameObject_SetActive(System.IntPtr L){
        //self
        var udptr = Lua.lua_touserdata(L,1);
        var active = Lua.lua_toboolean(L,2) != 0;
        var go = _objectCache[udptr] as GameObject;
        go.SetActive(active);
        return 0;
    }

    [MonoPInvokeCallback(typeof(LuaCSFunction))]
    private static int gc(System.IntPtr L){
        var udptr = Lua.lua_touserdata(L,1);
        if(_objectCache.Remove(udptr)){
            Debug.Log("userdata was gced: " + udptr);
        }else{
            Debug.LogError("cache missing:" + udptr);
        }
        return 0;
    }

    /// <summary>
    /// 演示如何注册一个c# class到lua并实现构造函数，在lua中返回其实例
    /// </summary>
    private void RegisterGameObjectToLua(System.IntPtr L){
        //===== 创建GameObject class =====
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
        //==== class的metatable设置完毕 =====//

        //===== 创建实例的metatable =======/
        
        //创建一个metatable并放到lua注册表中，同时压入栈顶
        //local metatable = {}
        //register["GameObject"] = metatable
        Lua.luaL_newmetatable(L,"GameObject");

        //metatable.__gc = gc -- 设置gc函数
        Lua.lua_pushstring(L,"__gc");
        Lua.lua_pushcfunction(L,gc);
        Lua.lua_settable(L,-3);

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

    }

    private void CallGameObjectSetActiveInLua(System.IntPtr L){
        var lua = 
        @"
        Debug.Log('CallGameObjectSetActiveInLua')
        local go = GameObject('GO')
        print(type(go))
        go:SetActive(false);
        ";
        DoLuaCode(L,lua);
    }

    /// <summary>
    /// 通过以上gameObject的示例子，我们这里写出一个通用的c#类注册函数
    /// </summary>
    private static void RegisterCSClass(System.IntPtr L,string className,Dictionary<string,LuaCSFunction> staticFuncs,LuaCSFunction constructor,Dictionary<string,LuaCSFunction> memberFuncs){
        //===== 创建GameObject class =====
        //local class = {}
        //L.push(class)
        Lua.lua_createtable(L,0,1);

        if(staticFuncs != null){
            foreach(var kv in staticFuncs){
                Lua.lua_pushstring(L,kv.Key);
                Lua.lua_pushcfunction(L,kv.Value);
                Lua.lua_settable(L,-3);
            }
        }

        //local classMeta = {}
        //L.push(classMeta)
        Lua.lua_createtable(L,0,1); 

        if(constructor != null){
            //register constructor
            Lua.lua_pushstring(L,"__call");
            Lua.lua_pushcfunction(L,constructor);
            Lua.lua_settable(L,-3);
        }

        Lua.lua_setmetatable(L,-2);
        //将栈顶元素弹出，设为全局变量
        Lua.lua_setglobal(L,className);
        //==== class的metatable设置完毕 =====//

        //===== 创建实例的metatable =======/
        //创建一个metatable并放到lua注册表中，同时压入栈顶
        //local metatable = {}
        //registery[className] = metatable
        Lua.luaL_newmetatable(L,className);

        //metatable.__gc = gc -- 设置gc函数
        Lua.lua_pushstring(L,"__gc");
        Lua.lua_pushcfunction(L,gc);
        Lua.lua_settable(L,-3);

        //local __index = {}
        Lua.lua_pushstring(L,"__index");
        Lua.lua_createtable(L,0,1);

        if(memberFuncs != null){
            foreach(var kv in memberFuncs){
                //__index[key] = func
                Lua.lua_pushstring(L,kv.Key);
                Lua.lua_pushcfunction(L,kv.Value);
                Lua.lua_settable(L,-3);
            }
        }

        //metatable.__index = __index
        Lua.lua_settable(L,-3);

        //弹出metatable
        Lua.lua_pop(L,1);
    }


    [MonoPInvokeCallback(typeof(LuaCSFunction))]
    private static int EventManager_Register(System.IntPtr L){
        //将L顶部元素弹出，加入到LUA_REGISTRY表中，完成lua vm中的引用。
        //返回的reference为一个int的索引
        //即LuaRegistery[reference] = luaCallback
        var reference = Lua.luaL_ref(L,(int)LUA_REGISTRY.Index);
        var luaFunc = new LuaFunction(_globalL,reference);
        EventManager.Register((value)=>{
            luaFunc.PCall(value);
        });
        return 0;
    }

    private void RegisterEventManagerToLua(System.IntPtr L){
        RegisterCSClass(L,"EventManager",new Dictionary<string, LuaCSFunction>(){
            {"Register",EventManager_Register}
        }, null,null);
    }

    private void DoEventManagerLuaCode(System.IntPtr L){
        var luaCode = 
        @"
            EventManager.Register(function(value)
                print('callback invoked ==>',tostring(value))
            end)
        ";
        DoLuaCode(L,luaCode);
    }


}




public class EventManager{
    private static event System.Action<int> _callbacks;
    public static void Register(System.Action<int> callback){
        _callbacks += callback;
    }

    public static void Dispatch(int value){
        if(_callbacks != null){
            _callbacks(value);
        }
    }

    public static void Clear(){
        _callbacks = null;
    }
}


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
